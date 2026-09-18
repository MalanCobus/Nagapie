using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Pages;

public partial class Home
{
    private bool consent, speechConsent, speechSupported, listening, saving;
    private string savedDraftText = "";
    private CancellationTokenSource? debounce, processing;
    private DotNetObjectReference<Home>? reference;
    protected override void OnInitialized()
    {
        savedDraftText = State.Draft.Text;
        if (!State.Settings.HasCompletedOnboarding)
        {
            Nav.NavigateTo("/welcome");
        }
    }

    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (!first)
        {
            return;
        }

        reference = DotNetObjectReference.Create(this);
        speechSupported = await JS.InvokeAsync<bool>("nagapie.speechSupported");
        await JS.InvokeVoidAsync("nagapie.setLanguage", State.Settings.Language);
        StateHasChanged();
    }

    private async Task InputAsync(ChangeEventArgs e)
    {
        State.Draft.Text = (e.Value?.ToString() ?? "")[..Math.Min(e.Value?.ToString()?.Length ?? 0, 5000)];
        State.Draft.Review = null;
        State.Draft.WasAiProcessed = false;
        if (State.DraftCommitted)
        {
            State.Draft.Id = Guid.NewGuid();
        }

        debounce?.Cancel();
        debounce?.Dispose();
        debounce = new();
        var token = debounce.Token;
        saving = true;
        Error = null;
        try
        {
            await Task.Delay(400, token);
            var textToSave = State.Draft.Text;
            await State.SaveDraftAsync();
            savedDraftText = textToSave;
            saving = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSException)
        {
            Error = ErrorCodes.Storage;
            saving = false;
        }
        catch (InvalidDataException ex)
        {
            Error = ex.Message;
            saving = false;
        }
        catch (ApiClientException exception)
        {
            Error = exception.Code;
            saving = false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Error = ErrorCodes.Storage;
            saving = false;
        }
    }

    private Task ExampleAsync() => Run(async () =>
    {
        State.Draft.Text = L["Dump_ExampleText"];
        await State.SaveDraftAsync();
        savedDraftText = State.Draft.Text;
    });
    private async Task SortAsync()
    {
        if (string.IsNullOrWhiteSpace(State.Draft.Text) || Busy)
        {
            return;
        }

        if (!State.Settings.HasConsentedToAiProcessing)
        {
            consent = true;
            return;
        }

        await ProcessAsync();
    }

    private async Task ConsentAsync()
    {
        consent = false;
        await Run(() => State.SaveSettingsAsync(State.Settings with { HasConsentedToAiProcessing = true }));
        if (Error is null)
        {
            await ProcessAsync();
        }
    }

    private async Task WithoutAiAsync()
    {
        consent = false;
        await SaveOneAsync();
    }

    private Task SaveOneAsync() => Run(async () =>
    {
        debounce?.Cancel();
        await StopSpeechAsync();
        await State.SaveDraftAsync();
        await State.SetReviewAsync([new() { Text = State.Draft.Text.Trim(), SourceDumpId = State.Draft.Id, InputMethod = State.Draft.InputMethod }], false);
        Nav.NavigateTo("/review/" + State.Draft.Id);
    });
    private Task ProcessAsync() => Run(async () =>
    {
        debounce?.Cancel();
        await StopSpeechAsync();
        await State.SaveDraftAsync();
        saving = false;
        if (State.Config.PaywallEnabled && State.Access.UnlockToken is null && State.SuccessfulAiDumps >= State.Config.FreeDumpLimit)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        if (!await JS.InvokeAsync<bool>("nagapie.online"))
        {
            Error = ErrorCodes.Offline;
            return;
        }

        processing?.Dispose();
        processing = new();
        var categories = State.Categories.Select(category => new CategoryReferenceDto(
            category.Id, category.Key, CategoryName(category), category.IsDefault)).ToList();
        var request = new ProcessDumpRequest(
            State.Draft.Id,
            State.Draft.Text.Trim(),
            State.Settings.Language,
            State.Draft.InputMethod,
            categories,
            State.Access.UnlockToken,
            State.SuccessfulAiDumps);
        try
        {
            var result = await Api.ProcessDumpAsync(request, processing.Token);
            var review = DumpReviewMapper.Map(result, State.Categories, State.Draft.InputMethod);
            await State.SetReviewAsync(review, true);
            Nav.NavigateTo("/review/" + State.Draft.Id);
        }
        catch (ApiClientException exception) when (exception.Code == ErrorCodes.PaywallRequired)
        {
            Nav.NavigateTo("/unlock");
        }
        catch (OperationCanceledException) when (processing.IsCancellationRequested)
        {
        }
        catch (System.Text.Json.JsonException)
        {
            Error = ErrorCodes.AiInvalidOutput;
        }
    });
    private void Cancel() => processing?.Cancel();
    private async Task SpeechAsync()
    {
        if (listening)
        {
            await StopSpeechAsync();
        }
        else if (!State.Settings.HasAcknowledgedSpeech)
        {
            speechConsent = true;
        }
        else
        {
            await StartSpeechAsync();
        }
    }

    private Task StartSpeechAsync() => Run(async () =>
    {
        speechConsent = false;
        await State.SaveSettingsAsync(State.Settings with
        {
            HasAcknowledgedSpeech = true
        });
        listening = await JS.InvokeAsync<bool>("nagapie.startSpeech", State.Settings.Language, reference);
        if (!listening)
        {
            Error = ErrorCodes.Speech;
        }
        else
        {
            State.Draft.InputMethod = "speech";
        }
    });
    private async Task StopSpeechAsync()
    {
        await JS.InvokeVoidAsync("nagapie.stopSpeech");
        listening = false;
    }

    [JSInvokable]
    public async Task Transcript(string text)
    {
        await InputAsync(new ChangeEventArgs { Value = (State.Draft.Text + " " + text).Trim() });
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task SpeechEnded(bool error)
    {
        listening = false;
        if (error)
        {
            Error = ErrorCodes.Speech;
        }

        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        debounce?.Cancel();
        debounce?.Dispose();
        processing?.Cancel();
        try
        {
            await StopSpeechAsync();
            if (saving)
            {
                await State.SaveDraftAsync();
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidDataException or ApiClientException or HttpRequestException or TaskCanceledException)
        {
        }

        processing?.Dispose();
        reference?.Dispose();
    }
}

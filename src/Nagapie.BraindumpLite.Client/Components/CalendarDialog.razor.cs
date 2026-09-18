using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Client.Domain;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class CalendarDialog
{
    [Parameter, EditorRequired]
    public BrainDumpItem Item { get; set; } = default!;

    [Parameter]
    public EventCallback Closed
    {
        get; set;
    }

    private DateTime date = DateTime.Today;
    private TimeOnly start = new(9, 0), end = new(9, 30);
    private bool allDay;
    private Task DownloadAsync() => Run(async () =>
    {
        if (!allDay && end <= start)
        {
            Error = ErrorCodes.Calendar;
            return;
        }

        var begins = allDay ? date.Date : DateTime.SpecifyKind(date.Date + start.ToTimeSpan(), DateTimeKind.Local);
        var ends = allDay ? date.Date.AddDays(1) : DateTime.SpecifyKind(date.Date + end.ToTimeSpan(), DateTimeKind.Local);
        var ics = CalendarExport.Create(Item.Text, CategoryName(Item.CategoryId), begins, ends, allDay);
        await JS.InvokeVoidAsync("nagapie.download", $"braindump-{date:yyyy-MM-dd}.ics", ics, "text/calendar;charset=utf-8");
        await Closed.InvokeAsync();
    });
}

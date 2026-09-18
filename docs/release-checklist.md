# Public release checklist

- [ ] Configure AI secret and verify actual model compatibility.
- [ ] Exercise Dutch and English dumps with four or more distinct thoughts, implied context, ambiguous time phrases, and prompt-injection attempts.
- [ ] Check the review experience with 30 items and 5,000-character input.
- [ ] Finalize actual provider processing region, retention and training terms.
- [ ] Supply operator identity, registered address, KvK and support ownership.
- [ ] Finalize privacy statement, terms, taxes and checkout wording.
- [ ] Configure HTTPS hosting, known proxy behavior, log retention, request-size and abuse budgets.
- [ ] Test Payhip purchase, invalid/disabled/refunded licenses, restoration after clearing account data, signing-key rotation.
- [ ] Test voice permissions, partial transcripts, denied microphone, interruption and five-minute timeout on real devices.
- [ ] Test published PWA offline error messages, installation and update behavior on iPhone Safari and Android Chrome.
- [ ] Test desktop Safari, Chrome, Edge and Firefox.
- [ ] Import timed/all-day exports into Apple Calendar, Google Calendar and Outlook.
- [ ] Complete real screenreader and keyboard-only review; verify contrast.
- [ ] Test multi-device conflict messages and session changes with real browsers.
- [ ] Enable payment only after the above checks pass.

- [ ] Create Azure SQL, grant the app database user schema-change permissions, and configure ConnectionStrings__Nagapie before publishing. Verify automatic startup migrations succeed.
- [ ] Configure database backup/restore and durable authentication key storage.
- [ ] Add email verification and password recovery with a configured email provider before wider public account registration.
- [ ] Decide and implement account deletion and retention policies.

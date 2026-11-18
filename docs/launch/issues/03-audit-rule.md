The security audit has 23 rules (AUD-001 … AUD-051), each with an official source. The list and the sources are in [docs/toolbox-sources.md](https://github.com/BitJacker/shiftwall/blob/main/docs/toolbox-sources.md).

**Task**
1. Pick one recommendation from the [FortiOS 8.0.0 Best Practices](https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/32eccc66-2e05-11f1-b31d-02356ffb40d9/FortiOS-8.0.0-Best_Practices.pdf) (or an RFC) that no rule covers yet and that can be checked from a configuration backup alone.
2. Comment here first with the recommendation, the page/section and the CLI setting you want to check, so we agree before you write code.
3. Add the rule to `src/Shiftwall.Features/Toolbox/Audit/AuditEngine.cs`, its English and Italian texts to `ToolboxMessages.resx` / `ToolboxMessages.it.resx`, a test, and a row in `docs/toolbox-sources.md`.

Remember: a value missing from a backup means "default"; only treat it as the documented default for versions whose CLI reference you checked.

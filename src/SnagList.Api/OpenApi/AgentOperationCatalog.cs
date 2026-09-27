namespace SnagList.Api.OpenApi;

public static class AgentOperationCatalog
{
    public static readonly IReadOnlyDictionary<string, string> OperationIdToMcpTool = new Dictionary<string, string>
    {
        ["ReportSnag"] = "report_snag",
        ["EditSnag"] = "edit_snag",
        ["WithdrawSnag"] = "withdraw_snag",
        ["AcknowledgeSnag"] = "acknowledge_snag",
        ["StartSnagWork"] = "start_snag_work",
        ["ResolveSnag"] = "resolve_snag",
        ["CloseSnag"] = "close_snag",
        ["RejectSnag"] = "reject_snag",
        ["AddSnagComment"] = "add_snag_comment",
        ["UploadSnagPhoto"] = "upload_snag_photo",
        ["GetSnagPhoto"] = "get_snag_photo",
        ["ListSnags"] = "list_snags",
        ["GetSnag"] = "get_snag",
        ["ListLocations"] = "list_locations",
        ["GetLocation"] = "get_location",
        ["CreateLocation"] = "create_location",
        ["UpdateLocation"] = "update_location",
        ["RetireLocation"] = "retire_location",
        ["GetMe"] = "get_me",
    };
}

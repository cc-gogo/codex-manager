namespace CodexConversationManager.Core.Inventory;

public enum InventoryReadStatus
{
    Completed,
    Pending,
    Failed
}

public sealed record InventoryDiagnostic(
    string Source,
    int RecordCount,
    DateTimeOffset ReadAt,
    string? Error,
    InventoryReadStatus Status = InventoryReadStatus.Completed)
{
    public string StatusText => Status switch
    {
        InventoryReadStatus.Pending => "正在后台核对",
        InventoryReadStatus.Failed => "读取失败",
        _ => "已完成"
    };
}

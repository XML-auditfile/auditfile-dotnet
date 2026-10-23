namespace XmlAuditfile.Auditfile.Validation;

/// <summary>Sees every row as it is emitted, and the end of the document.</summary>
internal interface IRowObserver
{
    void OnRow(AuditfileRow row);

    void OnEnd();
}

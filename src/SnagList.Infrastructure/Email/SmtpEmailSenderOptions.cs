namespace SnagList.Infrastructure.Email;

public sealed class SmtpEmailSenderOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string FromAddress { get; set; } = "";
    public bool UseTls { get; set; } = true;
}

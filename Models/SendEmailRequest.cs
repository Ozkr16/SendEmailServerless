namespace Zetill.Utils.Models
{
    public class SendEmailRequest
    {
        public string HCaptchaChallengeResponse { get; set; }
        public string Name { get; set; }
        public string Message { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
    }
}
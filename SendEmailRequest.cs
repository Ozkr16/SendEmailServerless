namespace Zetill.Utils
{
    public class SendEmailRequest
    {
        public string HCaptchaChallengeResponse { get; internal set; }
        public object Name { get; internal set; }
        public object Message { get; internal set; }
        public object Email { get; internal set; }
        public object PhoneNumber { get; internal set; }
    }
}
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Collections.Generic;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net;
using System.Text;

namespace Zetill.Utils
{
    public class SendEmail
    {
        private readonly HttpClient httpClient;
        private readonly ILogger<SendEmail> log;

        public SendEmail(HttpClient httpClient, ILogger<SendEmail> log)
        {
            this.httpClient = httpClient;
            this.log = log;
        }

        [FunctionName("SendEmail")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var form = await req.ReadFormAsync().ConfigureAwait(false);
            var hasAllProperties = form.TryGetValue("name", out var name);
            hasAllProperties &= form.TryGetValue("message", out var message);
            hasAllProperties &= form.TryGetValue("email", out var email);
            hasAllProperties &= form.TryGetValue("phone-number", out var phoneNumber);
            hasAllProperties &= form.TryGetValue("h-captcha-response", out var hCaptchaResponse);

            if (!hasAllProperties)
            {
                return new BadRequestResult();
            }

            SendEmailRequest request = new SendEmailRequest()
            {
                Name = name,
                Message = message,
                Email = email,
                PhoneNumber = phoneNumber,
                HCaptchaChallengeResponse = hCaptchaResponse,
            };

            var hCaptchaSecret = Environment.GetEnvironmentVariable("HCaptcha:Secret");

            IEnumerable<KeyValuePair<string, string>> hCaptchaParams = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("secret", hCaptchaSecret),
                new KeyValuePair<string, string>("response",request.HCaptchaChallengeResponse),
                // new KeyValuePair<string, string>("remoteip", /*Client's IP*/), // Optional.
            };

            var hCaptchaVerificationContent = new FormUrlEncodedContent(hCaptchaParams);
            var responseFromHCaptcha = await this.httpClient.PostAsync(@"https://hcaptcha.com/siteverify", hCaptchaVerificationContent).ConfigureAwait(false);

            if (!responseFromHCaptcha.IsSuccessStatusCode)
            {
                this.log.LogWarning($"Captcha validation was unsuccessfull. Response Status Code was: {responseFromHCaptcha.StatusCode}. Reason: {responseFromHCaptcha.ReasonPhrase}");
                return new BadRequestResult();
            }

            var sourceUserName = Environment.GetEnvironmentVariable("Sender:UserName");
            var sourceEmail = Environment.GetEnvironmentVariable("Sender:Address");

            var targetDomainName = Environment.GetEnvironmentVariable("Destination:DomainName");
            var targetUserName = Environment.GetEnvironmentVariable("Destination:UserName");
            var targetEmail = Environment.GetEnvironmentVariable("Destination:Address");


            var apiKey = Environment.GetEnvironmentVariable("SendGrid:Key");
            var sendgridClient = new SendGridClient(apiKey);
            var from = new EmailAddress(sourceEmail, sourceUserName);

            var subject = "Nuevo mensaje recibido";
            var to = new EmailAddress(targetEmail, targetUserName);

            var plainTextContent = "";

            var htmlContent = new StringBuilder(EmailTemplate.Template)
                                .Replace("{name}", request.Name)
                                .Replace("{email}", request.Email)
                                .Replace("{phone}", request.PhoneNumber)
                                .Replace("{message}", request.Message)
                                .ToString();

            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
            var sendgridResponse = await sendgridClient.SendEmailAsync(msg).ConfigureAwait(false);

            if (sendgridResponse.StatusCode != HttpStatusCode.Accepted)
            {
                var responseBody = await sendgridResponse.Body.ReadAsStringAsync().ConfigureAwait(false);
                this.log.LogError($"Unable to send email. Respose from SendGrid was: {responseBody}");

                return new BadRequestResult();
            }

            return new OkObjectResult("Success");
        }
    }
}

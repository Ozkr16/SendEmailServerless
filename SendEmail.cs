using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Collections.Generic;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net;

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

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync().ConfigureAwait(false);
            var request = JsonConvert.DeserializeObject<SendEmailRequest>(requestBody);

            var hCaptchaSecret = ""; // TODO: Read from Key Vault.

            IEnumerable<KeyValuePair<string, string>> hCaptchaParams = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("secret", hCaptchaSecret),
                new KeyValuePair<string, string>("response",request.HCaptchaChallengeResponse),
                // new KeyValuePair<string, string>("remoteip", /*Client's IP*/), // Optional.
            };

            var hCaptchaVerificationContent = new FormUrlEncodedContent(hCaptchaParams);
            var responseFromHCaptcha = await this.httpClient.PostAsync(@"https://hcaptcha.com/siteverify", hCaptchaVerificationContent).ConfigureAwait(false);

            if(!responseFromHCaptcha.IsSuccessStatusCode)
            {
                this.log.LogWarning($"Captcha validation was unsuccessfull. Response Status Code was: {responseFromHCaptcha.StatusCode}. Reason: {responseFromHCaptcha.ReasonPhrase}");
                return new BadRequestResult();
            }

            // var sourceDomainName = "Chrysalis-Tech.com"; // TODO: Read from config.
            var sourceUserName = "Chrysalis Technology"; // TODO: Read from config.
            var sourceEmail = "info@chrysalis-tech.com"; // TODO: Read from config.

            var targetDomainName = "Retagri.com"; // TODO: Read from config.
            var targetUserName = "Retagri S.A."; // TODO: Read from config.
            var targetEmail = "info@retagri.com"; // TODO: Read from config.



            var apiKey = Environment.GetEnvironmentVariable("NAME_OF_THE_ENVIRONMENT_VARIABLE_FOR_YOUR_SENDGRID_KEY");  // TODO: Read from Key Vault.
            var sendgridClient = new SendGridClient(apiKey);
            var from = new EmailAddress(sourceEmail, sourceUserName);
            
            var subject = "Nuevo mensaje recibido";
            var to = new EmailAddress(targetEmail, targetUserName);
            
            var plainTextContent = $@"Le comunicamos que ha recibido una nueva petición de contacto por medio de su sitio: {targetDomainName}."
                                 + $@"Le mensaje fue enviado por: {request.Name} y dice lo siguiente: {request.Message}."
                                 + $@"Información de contacto: Email: {request.Email} Número de Teléfono: {request.PhoneNumber}.";
            
            var htmlContent = ""; // "<strong>and easy to do anywhere, even with C#</strong>"; // TODO: Consider using HTML
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
            var sendgridResponse = await sendgridClient.SendEmailAsync(msg).ConfigureAwait(false);

            if(sendgridResponse.StatusCode != HttpStatusCode.Accepted)
            {
                var responseBody = await sendgridResponse.Body.ReadAsStringAsync().ConfigureAwait(false);
                this.log.LogError($"Unable to send email. Respose from SendGrid was: {responseBody}");
            }

            return new OkObjectResult("Success");
        }
    }
}

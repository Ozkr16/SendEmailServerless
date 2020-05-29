# SendEmailServerless
Azure Function to send emails using SendGrid, with hCaptcha anti-bot validation.

## Local Development Configuration

To be able to run the function locally, make sure you have a local.settings.json file with the following structure and config (create the file if necessary, in the same folder as the host.json file is located).

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "KeyVaultName":"local",
    "Mail:ParameterSeparatorChar": ",",
    "Mail:ExpectedParameters" :"<comma-separated-params-expected-in-request-and-replaced-in-email-template>", // sample: name,message,email,phone",
    "Mail:Subject" : "<email-subject>",
    "Sender:DomainName" : "<from-address-domain>",
    "Sender:UserName" : "<from-address-friendly-name>",
    "Sender:Address" : "<from-address>",
    "Destination:DomainName" : "<to-address-domain>",
    "Destination:UserName" : "<to-address-friendly-name>",
    "Destination:Address" : "<to-address>",
    "HCaptcha:Secret": "<HCaptcha-provided-secret>",
    "HCaptcha:VerificationEndpoint" : "<hchaptcha-verification-endpoint-url>", // https://hcaptcha.com/siteverify",
    "SendGrid:Key": "<SendGrid-provided-key>"
  }
}
```

You will also need to serve the Test/index.html file from a server and use a different domain than localhost (without this, the HCaptcha HTML element will not load). One of the easiest way to do it is by using Python SimpleHTTPServer:

```bash
# Using Python SimpleHTTPServer
cd SendEmailServerless/Test # cd to folder with the index.html we want to serve.
python -m SimpleHTTPServer 80
```

Finally, you will need to add an entry to your hosts file, located in /private/etc/hosts on macOS, in /etc/hosts on Linux or C:\Windows\System32\Drivers\etc\hosts on Windows.

The entry should look like:

```hosts
127.0.0.1   serverless.com
```

## Email Template

- Generated with: https://stripo.email/
- Minified with: https://minify-html.com
- Escaped with: https://www.csharpescaper.com
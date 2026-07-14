namespace GamaEdtech.Infrastructure.Provider.Email
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Data.Dto.Email;
    using GamaEdtech.Data.Dto.Provider.Email;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using Resend;

    using Svix.Exceptions;

    using static GamaEdtech.Common.Core.Constants;
    using static GamaEdtech.Data.Dto.Email.EmailDto;

    public sealed class ResendEmailProvider(Lazy<ILogger<ResendEmailProvider>> logger, Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider) : IEmailProvider
    {
        public EmailProviderType ProviderType => EmailProviderType.Resend;

        public async Task<ResultData<Common.Data.Void>> SendEmailAsync([NotNull] EmailRequestDto requestDto)
        {
            try
            {
                var client = CreateClient();
                var chunks = requestDto.Receivers.Chunk(100);
                List<string?> errors = [];
                foreach (var item in chunks)
                {
                    var response = await client.EmailBatchAsync(item.Select(t => new EmailMessage
                    {
                        From = requestDto.From,
                        HtmlBody = requestDto.Body,
                        Subject = requestDto.Subject,
                        To = EmailAddressList.From(t),
                    }));
                    if (!response.Success)
                    {
                        errors.Add(response.Exception?.Message);
                    }
                }

                return new(errors.Count == 0 ? OperationResult.Succeeded : OperationResult.Failed)
                {
                    Errors = errors.Count == 0 ? null : errors.Select(t => new Error { Message = t, }),
                };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<EmailDto>> ProccessInboundEmailAsync([NotNull] HttpRequest request)
        {
            try
            {
                if (!request.Body.CanSeek)
                {
                    request.EnableBuffering();
                }
                request.Body.Position = 0;
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                if (!request.Headers.TryGetValue("svix-id", out var svixId))
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Missing required header 'svix-id'", }] };
                }

                if (!request.Headers.TryGetValue("svix-timestamp", out var svixTimestamp))
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Missing required header 'svix-timestamp'", }] };
                }

                if (!request.Headers.TryGetValue("svix-signature", out var svixSignature))
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Missing required header 'svix-signature'", }] };
                }

                if (!long.TryParse(svixTimestamp.ToString(), out _))
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Invalid value 'svix-timestamp', expected long", }] };
                }

                try
                {
                    var webhook = new Svix.Webhook(configuration.Value.GetValue<string>("EmailProvider:Resend:Secret")!);
                    webhook.Verify(payload, new WebHeaderCollection
                    {
                        { "svix-id", svixId.ToString() },
                        { "svix-timestamp", svixTimestamp.ToString() },
                        { "svix-signature", svixSignature.ToString() }
                    });
                }
                catch (WebhookVerificationException)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Invalid Signature", }] };
                }

                var data = await request.ReadFromJsonAsync<Data.Dto.Provider.Email.ResendResponse<ResendEmailReceivedWebhookDto>>();
                if (!"email.received".Equals(data?.Type, StringComparison.OrdinalIgnoreCase) || data?.Data is null)
                {
                    return new(OperationResult.Succeeded);
                }

                var client = CreateClient();
                var content = await client.ReceivedEmailRetrieveAsync(data!.Data.EmailId);
                List<AttachmentDto>? lst = [];
                if (data.Data.Attachments?.Any() == true)
                {
                    foreach (var item in data.Data.Attachments)
                    {
                        var attachment = await client.EmailAttachmentRetrieveAsync(data!.Data.EmailId, item.Id);
                        if (string.IsNullOrEmpty(attachment.Content?.DownloadUrl))
                        {
                            continue;
                        }

                        var response = await httpProvider.Value.GetByteArrayAsync<IHttpRequest, IHttpRequest>(new()
                        {
                            Uri = attachment.Content.DownloadUrl,
                            Request = null,
                        });
                        if (response is not null)
                        {
                            lst.Add(new()
                            {
                                File = response,
                                Filename = item.Filename,
                                ContentType = item.ContentType,
                            });
                        }
                    }
                }
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        // Prefer the HTML part - the plain-text alternative is an auto-generated
                        // degraded rendering (images/links flattened to "[url]text") for clients that
                        // can't display HTML, not a faithful copy of the original message. Fall back
                        // to it only when the sender's email had no HTML part at all.
                        Body = string.IsNullOrEmpty(content.Content.HtmlBody) ? content.Content.TextBody : content.Content.HtmlBody,
                        From = data.Data.From,
                        To = data.Data.To,
                        Subject = data.Data.Subject,
                        Attachments = lst,
                    }
                };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private IResend CreateClient() => ResendClient.Create(configuration.Value.GetValue<string>("EmailProvider:Resend:ApiToken")!);
    }
}

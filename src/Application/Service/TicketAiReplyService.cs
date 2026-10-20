namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using AngleSharp.Html.Parser;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Ticket;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Ticket;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed partial class TicketAiReplyService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<TicketAiReplyService>> localizer
        , Lazy<ILogger<TicketAiReplyService>> logger, Lazy<ITicketService> ticketService, Lazy<IAiService> aiService)
        : LocalizableServiceBase<TicketAiReplyService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ITicketAiReplyService
    {
        private const string HandbookResourceName = "SupportHandbook.md";
        private const int MaxTokens = 1000;
        private const double Temperature = 0.3;

        private static readonly Lazy<string> Handbook = new(ReadHandbook);

        private static readonly string[] GenerateRules =
        [
            "You are a GamaTrain admin support agent.",
            "Write a direct reply to the customer based on the ticket below.",
            "Use the GamaTrain support handbook below for how the website works: menus, buttons, steps and links.",
            "When the customer asks how to do something, give the exact steps from the handbook.",
            "Use the customer name when it is available.",
            "Keep the tone professional, warm, clear, and concise.",
            "Do not invent facts, prices, discounts, timelines, links, policies, or technical details that are not in the ticket or the handbook.",
            "Never state anything the handbook marks as TODO(confirm); say the team will check and follow up instead.",
            "Follow the handbook sections \"Always escalate\" and \"Never\".",
            "If the issue cannot be resolved with the ticket and the handbook, politely ask the customer for the exact missing details.",
            "Do not mention that you are an AI, the handbook, or that you received instructions.",
        ];

        private static readonly string[] PolishRules =
        [
            "You are a professional English writing editor for a GamaTrain admin support reply.",
            "Polish the admin reply given at the end of the user message.",
            "Improve grammar, spelling, word choice, clarity, and professional tone.",
            "Preserve the original meaning and intent exactly.",
            "Use the GamaTrain support handbook below only to correct menu, button and page names, and to turn a mention of a page into its URL from the handbook \"Links\" table.",
            "Do not add new facts, steps, promises, prices, discounts, timelines, policies, or technical details.",
            "Remove or soften any rude, offensive, aggressive, or unprofessional wording while keeping the message clear.",
            "The ticket is included for context only; do not answer it yourself.",
        ];

        private static readonly string[] HtmlFormatRules =
        [
            "Format the reply as clean HTML suitable for a rich text editor.",
            "Use <p> tags for the greeting, each paragraph, and the closing; use <ol> or <ul> for steps.",
            "Put the greeting in its own paragraph, for example: <p>Dear [Customer Name],</p>.",
            "Put the closing in its own final paragraph, for example: <p>Best regards,<br>GamaTrain Support</p>.",
            "Links must be <a href=\"...\"> tags using full URLs from the handbook.",
            "Do not use Markdown and do not wrap the reply in a fenced code block.",
            "Do not include scripts, styles, images, tables, or unnecessary HTML attributes.",
            "Return only the final HTML reply body.",
        ];

        public async Task<ResultData<string>> GenerateReplyAsync([NotNull] TicketAiReplyRequestDto requestDto)
        {
            try
            {
                var draft = ToPlainText(requestDto.Draft);
                if (requestDto.Mode == TicketAiReplyMode.Polish && string.IsNullOrWhiteSpace(draft))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["TicketAiReplyDraftRequired"] },] };
                }

                var ticketResult = await ticketService.Value.GetTicketAsync(new IdEqualsSpecification<Ticket, long>(requestDto.TicketId));
                if (ticketResult.OperationResult is not OperationResult.Succeeded || ticketResult.Data is null)
                {
                    return new(ticketResult.OperationResult) { Errors = ticketResult.Errors };
                }

                var repliesResult = await ticketService.Value.GetTicketReplysAsync(new TicketIdEqualsSpecification(requestDto.TicketId));
                var replies = repliesResult.OperationResult is OperationResult.Succeeded ? repliesResult.Data ?? [] : [];

                var rules = requestDto.Mode == TicketAiReplyMode.Polish ? PolishRules : GenerateRules;
                var systemPrompt = string.Join('\n', [.. rules, .. HtmlFormatRules, string.Empty, "# GamaTrain support handbook", string.Empty, Handbook.Value]);

                List<string> userPrompt = [.. BuildTicketContext(ticketResult.Data, replies)];
                if (requestDto.Mode == TicketAiReplyMode.Polish)
                {
                    userPrompt.AddRange([string.Empty, "Admin reply to polish:", draft!]);
                }

                var completion = await aiService.Value.CompleteAsync(new()
                {
                    SystemPrompt = systemPrompt,
                    UserPrompt = string.Join('\n', userPrompt),
                    MaxTokens = MaxTokens,
                    Temperature = Temperature,
                });
                return completion.OperationResult is not OperationResult.Succeeded || completion.Data?.Content is null
                    ? new(OperationResult.Failed) { Errors = completion.Errors }
                    : new(OperationResult.Succeeded) { Data = StripCodeFence(completion.Data.Content) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        internal static IEnumerable<string> BuildTicketContext([NotNull] TicketDto ticket, [NotNull] IEnumerable<TicketReplyDto> replies)
        {
            var conversation = replies.Any()
                ? string.Join("\n\n", replies.Select((reply, index) => string.Join('\n',
                    $"Reply {index + 1}:",
                    $"From: {(reply.BySupport ? "Support team" : "Customer")}",
                    $"Date: {reply.CreationDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
                    $"Body: {ToPlainText(reply.Body)}")))
                : "No previous replies.";
            var body = ToPlainText(ticket.Body);

            return
            [
                $"Customer name: {(string.IsNullOrWhiteSpace(ticket.FullName) ? "Unknown" : ticket.FullName)}",
                $"Customer email: {(string.IsNullOrWhiteSpace(ticket.Email) ? "Unknown" : ticket.Email)}",
                $"Subject: {(string.IsNullOrWhiteSpace(ticket.Subject) ? "No subject" : ticket.Subject)}",
                $"Original message: {(body.Length > 0 ? body : "No body")}",
                string.Empty,
                "Previous conversation:",
                conversation,
            ];
        }

        /// <summary>
        /// Editor HTML to readable text for the prompt: block ends become line breaks, tags are dropped and entities
        /// decoded. (<c>SanitizePlainText</c> isn't used: it is meant for short titles and flattens line breaks.)
        /// </summary>
        internal static string ToPlainText(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var withBreaks = BlockEnd().Replace(html, "$0\n");
            var text = new HtmlParser().ParseDocument(withBreaks).Body?.TextContent ?? string.Empty;

            return ExtraBlankLines().Replace(text, "\n\n").Trim();
        }

        /// <summary>Models sometimes wrap HTML in ```html fences despite being told not to.</summary>
        internal static string StripCodeFence(string text) => CodeFence().Replace(text.Trim(), string.Empty).Trim();

        private static string ReadHandbook()
        {
            using var stream = typeof(TicketAiReplyService).Assembly.GetManifestResourceStream(HandbookResourceName)
                ?? throw new InvalidOperationException($"Missing embedded resource {HandbookResourceName}.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        [GeneratedRegex(@"<br\s*/?>|</(p|li|div|h[1-6])>", RegexOptions.IgnoreCase, 1000)]
        private static partial Regex BlockEnd();

        [GeneratedRegex(@"\n{3,}", RegexOptions.None, 1000)]
        private static partial Regex ExtraBlankLines();

        [GeneratedRegex(@"^```(?:html)?\s*|\s*```$", RegexOptions.IgnoreCase, 1000)]
        private static partial Regex CodeFence();
    }
}

namespace GamaEdtech.Infrastructure.Provider.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// OpenAI chat completions. Settings live under <c>Ai:OpenAi</c> (<c>Uri</c>, <c>Model</c>, <c>MaxTokens</c>,
    /// <c>ApiKey</c>); the key is never committed, it is supplied by the deployment's configuration.
    /// </summary>
    public sealed class OpenAiProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider
        , Lazy<IStringLocalizer<OpenAiProvider>> localizer, Lazy<ILogger<OpenAiProvider>> logger)
        : InfrastructureBase<OpenAiProvider>(httpProvider, localizer, logger), IAiProvider
    {
        private const int DefaultMaxTokens = 1000;
        private const int TimeoutMilliseconds = 60_000;

        public AiProviderType ProviderType => AiProviderType.OpenAi;

        public async Task<ResultData<ChatCompletionResponseDto>> CompleteAsync([NotNull] ChatCompletionRequestDto requestDto)
        {
            try
            {
                var apiKey = configuration.Value.GetValue<string?>("Ai:OpenAi:ApiKey");
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["AiProviderNotConfigured"] },] };
                }

                List<OpenAiChatMessage> messages = [];
                if (!string.IsNullOrWhiteSpace(requestDto.SystemPrompt))
                {
                    messages.Add(new() { Role = "system", Content = requestDto.SystemPrompt });
                }

                messages.Add(new() { Role = "user", Content = requestDto.UserPrompt });

                var body = new OpenAiChatRequest
                {
                    Model = configuration.Value.GetValue<string?>("Ai:OpenAi:Model"),
                    Messages = messages,
                    MaxTokens = requestDto.MaxTokens ?? configuration.Value.GetValue("Ai:OpenAi:MaxTokens", DefaultMaxTokens),
                    Temperature = requestDto.Temperature,
                };

                // OpenAI answers errors with a non-200 status and an "error" object, which the default JSON decode
                // reads into OpenAiChatResponse.Error.
                var response = await HttpProvider.Value.PostAsync<OpenAiChatRequest, OpenAiChatResponse, OpenAiChatRequest>(new()
                {
                    Uri = configuration.Value.GetValue<string?>("Ai:OpenAi:Uri"),
                    Request = body,
                    Body = body,
                    HeaderParameters = [("Authorization", $"Bearer {apiKey}"),],
                    Timeout = TimeoutMilliseconds,
                });

                var content = response?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    var message = response?.Error?.Message;
                    Logger.Value.LogError("OpenAI chat completion failed: {Message}", message ?? "empty response");
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["AiResponseFailed"] },] };
                }

                return new(OperationResult.Succeeded) { Data = new() { Content = content } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }
    }
}

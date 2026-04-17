// Copyright (c) Helsincy. All rights reserved.

using System.Net;
using System.Text;

namespace Launcher.Tests.Unit.Helpers;

/// <summary>
/// 可配置响应的 Mock HTTP 消息处理器。用于单元测试。
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>已接收到的请求列表</summary>
    public List<HttpRequestMessage> ReceivedRequests { get; } = [];

    /// <summary>已接收到的请求体快照</summary>
    public List<string?> ReceivedRequestBodies { get; } = [];

    /// <summary>入队一个预设响应</summary>
    public void EnqueueResponse(HttpStatusCode statusCode, string? jsonContent = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (jsonContent is not null)
            response.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        _responses.Enqueue(response);
    }

    /// <summary>入队一个会抛异常的响应</summary>
    public void EnqueueException(Exception exception)
    {
        _responses.Enqueue(new FaultedResponse(exception));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ReceivedRequests.Add(request);
        ReceivedRequestBodies.Add(request.Content is null
            ? null
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);

        if (_responses.Count == 0)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var response = _responses.Dequeue();
        if (response is FaultedResponse faulted)
            return Task.FromException<HttpResponseMessage>(faulted.Exception);

        return Task.FromResult(response);
    }

    private sealed class FaultedResponse : HttpResponseMessage
    {
        public Exception Exception { get; }
        public FaultedResponse(Exception exception) : base(HttpStatusCode.InternalServerError)
        {
            Exception = exception;
        }
    }
}

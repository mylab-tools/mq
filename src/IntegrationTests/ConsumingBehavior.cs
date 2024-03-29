﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyLab.Mq;
using MyLab.Mq.PubSub;
using Newtonsoft.Json;
using Tests.Common;
using TestServer;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests
{
    public partial class ConsumingBehavior : IClassFixture<WebApplicationFactory<Startup>>
    {

        [Fact]
        public async Task ShouldConsumeSimpleMessage()
        {
            //Arrange
            using var queue = CreateTestQueue();
            using var client = CreateTestClientWithSingleConsumer<TestSimpleMqLogic>(queue, _output);

            //Act
            await PublishMessages(queue, "foo");
            
            var resp = await client.GetAsync("test/single");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<SingleMessageTestBox>(respStr);

            //Assert
            Assert.NotNull(testBox.AckMsg);
            Assert.Equal("foo", testBox.AckMsg.Payload.Content);
            Assert.Null(testBox.RejectedMsg);

            await PrintStatus(client);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ShouldNotConsumeWhenNotConfigured(bool optional)
        {
            //Arrange
            using var queue = CreateTestQueue();

            var consumer = new MqConsumer<TestMqMsg, TestSimpleMqLogic>(queue.Name);

            using var client = _appFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddLogging(b => b
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddFilter(level => level >= LogLevel.Debug)
                        .AddXUnit(_output));
                    services.AddMqConsuming(registrar =>
                    {
                        registrar.RegisterConsumer(consumer);
                    }, optional);
                });
            }).CreateClient();

            //Act
            await PublishMessages(queue, "foo");

            var resp = await client.GetAsync("test/single");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<SingleMessageTestBox>(respStr);

            //Assert
            Assert.Null(testBox.AckMsg);
        }

        [Fact]
        public async Task ShouldRejectSimpleMessage()
        {
            //Arrange
            using var queue = CreateTestQueue();
            using var client = CreateTestClientWithSingleConsumer<TestSimpleMqLogicWithReject>(queue, _output);

            //Act
            await PublishMessages(queue, "foo");

            var resp = await client.GetAsync("test/single-with-reject");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<SingleMessageTestBox>(respStr);

            //Assert
            Assert.Null(testBox.AckMsg);
            Assert.NotNull(testBox.RejectedMsg);
            Assert.Equal("foo", testBox.RejectedMsg.Payload.Content);

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldConsumeMessageBatch()
        {
            //Arrange
            using var queue = CreateTestQueue();
            using var client = CreateTestClientWithBatchConsumer<TestBatchMqLogic>(queue, _output);

            //Act
            await PublishMessages(queue, "foo", "bar");

            var resp = await client.GetAsync("test/batch");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<BatchMessageTestBox>(respStr);

            ////Assert
            Assert.Null(testBox.RejectedMsgs);
            Assert.NotNull(testBox.AckMsgs);
            Assert.Equal(2, testBox.AckMsgs.Length);
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "foo");
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "bar");

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldConsumeIncompleteMessageBatch()
        {
            //Arrange
            using var queue = CreateTestQueue();
            using var client = CreateTestClientWithBatchConsumer<TestBatchMqLogic>(queue, _output, size: 5);

            //Act
            await PublishMessages(queue, "foo", "bar");
            await Task.Delay(TimeSpan.FromSeconds(3));

            var resp = await client.GetAsync("test/batch");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<BatchMessageTestBox>(respStr);

            ////Assert
            Assert.Null(testBox.RejectedMsgs);
            Assert.Equal(2, testBox.AckMsgs.Length);
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "foo");
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "bar");

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldRejectMessageBatch()
        {
            //Arrange
            using var queue = CreateTestQueue();
            using var client = CreateTestClientWithBatchConsumer<TestBatchMqLogicWithReject>(queue, _output);

            //Act
            await PublishMessages(queue, "foo", "bar");

            var resp = await client.GetAsync("test/batch-with-reject");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<BatchMessageTestBox>(respStr);

            //Assert
            Assert.Null(testBox.AckMsgs);
            Assert.NotNull(testBox.RejectedMsgs);
            Assert.Equal(2, testBox.RejectedMsgs.Length);
            Assert.Contains(testBox.RejectedMsgs, m => m.Payload.Content == "foo");
            Assert.Contains(testBox.RejectedMsgs, m => m.Payload.Content == "bar");

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldUseIncomingMessageScopeServices()
        {
            //Arrange
            using var queue = CreateTestQueue();
            using var client = CreateTestClientWithSingleConsumer<MqLogicWithScopedDependency>(queue, _output);

            //Act
            await PublishMessages(queue, "foo");

            var resp = await client.GetAsync("test/from-scope");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            //Assert
            Assert.Equal("foo", respStr);

            await PrintStatus(client);
        }
    }
}

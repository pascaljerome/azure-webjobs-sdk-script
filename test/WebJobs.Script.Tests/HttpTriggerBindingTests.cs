﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.Script.Binding.Http;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpTriggerBindingTests
    {
        public interface ITestInterface
        {
        }

        [Theory]
        [InlineData(typeof(TestPoco), true)]
        [InlineData(typeof(TestStruct), true)]
        [InlineData(typeof(ITestInterface), false)]
        [InlineData(typeof(Guid), false)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(string), false)]
        public void IsValidUserType_ReturnsExpectedValue(Type type, bool expected)
        {
            Assert.Equal(expected, HttpTriggerAttributeBindingProvider.IsValidUserType(type));
        }

        [Fact]
        public async Task GetRequestBindingDataAsync_ReadsFromBody()
        {
            string input = "{ test: 'testing', baz: 123, nestedArray: [ { nesting: 'yes' } ], nestedObject: { a: 123, b: 456 } }";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://functions/test");
            request.Content = new StringContent(input);
            var bindingData = await HttpTriggerAttributeBindingProvider.HttpTriggerBinding.GetRequestBindingDataAsync(request);

            Assert.Equal(2, bindingData.Count);
            Assert.Equal("testing", bindingData["test"]);
            Assert.Equal("123", bindingData["baz"]);
        }

        [Fact]
        public async Task GetRequestBindingDataAsync_ReadsFromQueryString()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://functions/test?name=Mathew%20Charles&location=Seattle");

            var bindingData = await HttpTriggerAttributeBindingProvider.HttpTriggerBinding.GetRequestBindingDataAsync(request);

            Assert.Equal(2, bindingData.Count);
            Assert.Equal("Mathew Charles", bindingData["name"]);
            Assert.Equal("Seattle", bindingData["location"]);

            request = new HttpRequestMessage(HttpMethod.Post, "http://functions/test");
            request.Content = new StringContent(string.Empty);
            bindingData = await HttpTriggerAttributeBindingProvider.HttpTriggerBinding.GetRequestBindingDataAsync(request);

            Assert.Equal(0, bindingData.Count);
        }

        [Fact]
        public async Task BindAsync_Poco_FromRequestBody()
        {
            ParameterInfo parameterInfo = GetType().GetMethod("TestPocoFunction").GetParameters()[0];
            HttpTriggerAttributeBindingProvider.HttpTriggerBinding binding = new HttpTriggerAttributeBindingProvider.HttpTriggerBinding(parameterInfo, true);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://functions/myfunc?code=abc123");

            JObject requestBody = new JObject
            {
                { "Name", "Mathew Charles" },
                { "Location", "Seattle" }
            };
            request.Content = new StringContent(requestBody.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            FunctionBindingContext functionContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, new TestTraceWriter(TraceLevel.Verbose));
            ValueBindingContext context = new ValueBindingContext(functionContext, CancellationToken.None);
            ITriggerData triggerData = await binding.BindAsync(request, context);

            Assert.Equal(2, triggerData.BindingData.Count);
            Assert.Equal("Mathew Charles", triggerData.BindingData["Name"]);
            Assert.Equal("Seattle", triggerData.BindingData["Location"]);

            TestPoco testPoco = (TestPoco)triggerData.ValueProvider.GetValue();
            Assert.Equal("Mathew Charles", testPoco.Name);
            Assert.Equal("Seattle", testPoco.Location);
        }

        [Fact]
        public async Task BindAsync_Poco_WebHookData()
        {
            ParameterInfo parameterInfo = GetType().GetMethod("TestPocoFunction").GetParameters()[0];
            HttpTriggerAttributeBindingProvider.HttpTriggerBinding binding = new HttpTriggerAttributeBindingProvider.HttpTriggerBinding(parameterInfo, true);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://functions/myfunc?code=abc123");
            TestPoco testPoco = new TestPoco
            {
                Name = "Mathew Charles",
                Location = "Seattle"
            };
            request.Properties.Add(ScriptConstants.AzureFunctionsWebHookDataKey, testPoco);

            FunctionBindingContext functionContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, new TestTraceWriter(TraceLevel.Verbose));
            ValueBindingContext context = new ValueBindingContext(functionContext, CancellationToken.None);
            ITriggerData triggerData = await binding.BindAsync(request, context);

            Assert.Equal(2, triggerData.BindingData.Count);
            Assert.Equal("Mathew Charles", triggerData.BindingData["Name"]);
            Assert.Equal("Seattle", triggerData.BindingData["Location"]);

            TestPoco result = (TestPoco)triggerData.ValueProvider.GetValue();
            Assert.Same(testPoco, result);
        }

        [Fact]
        public async Task BindAsync_Poco_FromQueryParameters()
        {
            ParameterInfo parameterInfo = GetType().GetMethod("TestPocoFunction").GetParameters()[0];
            HttpTriggerAttributeBindingProvider.HttpTriggerBinding binding = new HttpTriggerAttributeBindingProvider.HttpTriggerBinding(parameterInfo, true);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions/myfunc?code=abc123&Name=Mathew%20Charles&Location=Seattle");

            FunctionBindingContext functionContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, new TestTraceWriter(TraceLevel.Verbose));
            ValueBindingContext context = new ValueBindingContext(functionContext, CancellationToken.None);
            ITriggerData triggerData = await binding.BindAsync(request, context);

            Assert.Equal(2, triggerData.BindingData.Count);
            Assert.Equal("Mathew Charles", triggerData.BindingData["Name"]);
            Assert.Equal("Seattle", triggerData.BindingData["Location"]);

            TestPoco testPoco = (TestPoco)triggerData.ValueProvider.GetValue();
            Assert.Equal("Mathew Charles", testPoco.Name);
            Assert.Equal("Seattle", testPoco.Location);
        }

        [Fact]
        public async Task BindAsync_HttpRequestMessage_FromRequestBody()
        {
            ParameterInfo parameterInfo = GetType().GetMethod("TestHttpRequestMessageFunction").GetParameters()[0];
            HttpTriggerAttributeBindingProvider.HttpTriggerBinding binding = new HttpTriggerAttributeBindingProvider.HttpTriggerBinding(parameterInfo, false);

            // we intentionally do not send a content type on the request
            // to ensure that we can still extract binding data in such cases
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://functions/myfunc?code=abc123");
            JObject requestBody = new JObject
            {
                { "Name", "Mathew Charles" },
                { "Location", "Seattle" }
            };
            request.Content = new StringContent(requestBody.ToString());

            FunctionBindingContext functionContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, new TestTraceWriter(TraceLevel.Verbose));
            ValueBindingContext context = new ValueBindingContext(functionContext, CancellationToken.None);
            ITriggerData triggerData = await binding.BindAsync(request, context);

            Assert.Equal(2, triggerData.BindingData.Count);
            Assert.Equal("Mathew Charles", triggerData.BindingData["Name"]);
            Assert.Equal("Seattle", triggerData.BindingData["Location"]);

            HttpRequestMessage result = (HttpRequestMessage)triggerData.ValueProvider.GetValue();
            Assert.Same(request, result);
        }

        [Fact]
        public async Task BindAsync_HttpRequestMessage_FromQueryParameters()
        {
            ParameterInfo parameterInfo = GetType().GetMethod("TestHttpRequestMessageFunction").GetParameters()[0];
            HttpTriggerAttributeBindingProvider.HttpTriggerBinding binding = new HttpTriggerAttributeBindingProvider.HttpTriggerBinding(parameterInfo, false);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions/myfunc?code=abc123&Name=Mathew%20Charles&Location=Seattle");

            FunctionBindingContext functionContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, new TestTraceWriter(TraceLevel.Verbose));
            ValueBindingContext context = new ValueBindingContext(functionContext, CancellationToken.None);
            ITriggerData triggerData = await binding.BindAsync(request, context);

            Assert.Equal(2, triggerData.BindingData.Count);
            Assert.Equal("Mathew Charles", triggerData.BindingData["Name"]);
            Assert.Equal("Seattle", triggerData.BindingData["Location"]);

            HttpRequestMessage result = (HttpRequestMessage)triggerData.ValueProvider.GetValue();
            Assert.Same(request, result);
        }

        [Fact]
        public async Task BindAsync_String()
        {
            ParameterInfo parameterInfo = GetType().GetMethod("TestStringFunction").GetParameters()[0];
            HttpTriggerAttributeBindingProvider.HttpTriggerBinding binding = new HttpTriggerAttributeBindingProvider.HttpTriggerBinding(parameterInfo, false);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://functions/myfunc?code=abc123");
            request.Content = new StringContent("This is a test");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/text");

            FunctionBindingContext functionContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, new TestTraceWriter(TraceLevel.Verbose));
            ValueBindingContext context = new ValueBindingContext(functionContext, CancellationToken.None);
            ITriggerData triggerData = await binding.BindAsync(request, context);

            Assert.Equal(0, triggerData.BindingData.Count);

            string result = (string)triggerData.ValueProvider.GetValue();
            Assert.Equal("This is a test", result);
        }

        public void TestPocoFunction(TestPoco poco)
        {
        }

        public void TestHttpRequestMessageFunction(HttpRequestMessage req)
        {
        }

        public void TestStringFunction(string body)
        {
        }

        public struct TestStruct
        {
            public string Name { get; set; }
            public string Location { get; set; }
        }

        public class TestPoco
        {
            public string Name { get; set; }
            public string Location { get; set; }
        }
    }
}

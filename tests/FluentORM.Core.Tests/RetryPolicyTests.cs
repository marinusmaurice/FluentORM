using System;
using System.Threading.Tasks;
using FluentORM.Core.Configuration;
using FluentORM.Core.Execution;
using Xunit;
using FluentAssertions;

namespace FluentORM.Core.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task RetryPolicy_NoRetries_ExecutesOnce()
    {
        var options = new FluentOrmOptions { RetryAttempts = 0 };
        var policy = new RetryPolicy(options);
        int callCount = 0;

        await policy.ExecuteAsync(async ct => { callCount++; await Task.CompletedTask; return 0; });

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task RetryPolicy_TransientError_RetriesAndSucceeds()
    {
        var options = new FluentOrmOptions { RetryAttempts = 2, RetryBackoff = BackoffStrategy.Linear };
        var policy = new RetryPolicy(options);
        int callCount = 0;

        var result = await policy.ExecuteAsync(async _ =>
        {
            callCount++;
            if (callCount < 3)
                throw new TransientDbException("connection timeout");
            return 42;
        });

        result.Should().Be(42);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task RetryPolicy_NonTransientError_DoesNotRetry()
    {
        var options = new FluentOrmOptions { RetryAttempts = 3 };
        var policy = new RetryPolicy(options);
        int callCount = 0;

        Func<Task> act = async () => await policy.ExecuteAsync<int>(async ct =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new InvalidOperationException("logic error");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        callCount.Should().Be(1); // no retries for non-transient
    }

    [Fact]
    public async Task RetryPolicy_ExceedsAttempts_ThrowsLastException()
    {
        var options = new FluentOrmOptions { RetryAttempts = 2, RetryBackoff = BackoffStrategy.Linear };
        var policy = new RetryPolicy(options);
        int callCount = 0;

        Func<Task> act = async () => await policy.ExecuteAsync<int>(async ct =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new TransientDbException("connection timeout");
        });

        await act.Should().ThrowAsync<TransientDbException>();
        callCount.Should().Be(3); // initial + 2 retries
    }
}

// Concrete DbException for testing transient failures
internal sealed class TransientDbException(string message) : System.Data.Common.DbException(message) { }

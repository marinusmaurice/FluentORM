using System;
using System.Threading;
using FluentORM.Core.Diagnostics;
using FluentORM.Core.Exceptions;
using Xunit;
using FluentAssertions;

namespace FluentORM.Core.Tests;

public class NPlusOneTests
{
    [Fact]
    public void QueryTracker_BelowThreshold_DoesNotThrow()
    {
        var tracker = new QueryTracker(WhenDetected.Throw);
        QueryTracker.SetCurrent(tracker);

        // Track 5 times — threshold is 5, triggers on > 5 (i.e., 6th call)
        for (int i = 0; i < 5; i++)
            tracker.Track(typeof(Pest));

        QueryTracker.SetCurrent(null);
    }

    [Fact]
    public void QueryTracker_AboveThreshold_Throw_ThrowsNPlusOne()
    {
        var tracker = new QueryTracker(WhenDetected.Throw);

        Action act = () =>
        {
            for (int i = 0; i < 10; i++)
                tracker.Track(typeof(Pest));
        };

        act.Should().Throw<NPlusOneException>()
            .Which.EntityType.Should().Be(typeof(Pest));
    }

    [Fact]
    public void QueryTracker_DifferentEntityTypes_TrackedSeparately()
    {
        var tracker = new QueryTracker(WhenDetected.Throw);

        // Track different entity types — should not trigger N+1 for each individually
        for (int i = 0; i < 4; i++)
        {
            tracker.Track(typeof(Pest));
            tracker.Track(typeof(Field));
        }
        // No exception should be thrown
    }

    [Fact]
    public void QueryTracker_WarnMode_DoesNotThrow()
    {
        var tracker = new QueryTracker(WhenDetected.Warn);

        // Should log warning but not throw even above threshold
        Action act = () =>
        {
            for (int i = 0; i < 10; i++)
                tracker.Track(typeof(Pest));
        };

        act.Should().NotThrow();
    }
}

// Taj's COI Mods | ProfilerPhaseContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using TajsCOI.Profiler;
using TajsCOI.Profiler.Core;
using Xunit;

namespace TajsCOI.Tests.RuntimeContracts
{
    /// <summary>
    ///     Regression coverage for event-instance phase attribution.  Two events intentionally
    ///     share the same CLR type so a type-keyed registry would report the wrong phase.
    /// </summary>
    public sealed class ProfilerPhaseContractTests
    {
        [Fact]
        public void SameConcreteTypeUsesEventInstanceAttributionAndNestedScopesRestore()
        {
            object simulationEvent = new();
            object renderEvent = new();
            RuntimeTracePhaseContext.RegisterEvent(simulationEvent, RuntimeTracePhase.SimUpdate);
            RuntimeTracePhaseContext.RegisterEvent(renderEvent, RuntimeTracePhase.Render);

            RuntimeTracePhaseContext.PhaseScope outer =
                RuntimeTracePhaseContext.Enter(simulationEvent, RuntimeTracePhase.Unknown);
            try
            {
                Assert.Equal(RuntimeTracePhase.SimUpdate, RuntimeTracePhaseContext.CurrentPhase);

                RuntimeTracePhaseContext.PhaseScope inner =
                    RuntimeTracePhaseContext.Enter(renderEvent, RuntimeTracePhase.Unknown);
                try
                {
                    Assert.Equal(RuntimeTracePhase.Render, RuntimeTracePhaseContext.CurrentPhase);
                }
                finally
                {
                    inner.Dispose();
                }

                Assert.Equal(RuntimeTracePhase.SimUpdate, RuntimeTracePhaseContext.CurrentPhase);
            }
            finally
            {
                outer.Dispose();
            }

            Assert.Equal(RuntimeTracePhase.Unknown, RuntimeTracePhaseContext.CurrentPhase);
            Assert.Equal(
                RuntimeTracePhase.Unknown,
                RuntimeTracePhaseContext.Resolve(new object(), RuntimeTracePhase.Unknown));
        }

        [Fact]
        public void ExceptionPathRestoresThePriorThreadLocalPhase()
        {
            object simulationEvent = new();
            RuntimeTracePhaseContext.RegisterEvent(simulationEvent, RuntimeTracePhase.SimUpdate);

            Action invokeWithException = () =>
            {
                RuntimeTracePhaseContext.PhaseScope scope =
                    RuntimeTracePhaseContext.Enter(simulationEvent, RuntimeTracePhase.Unknown);
                try
                {
                    Assert.Equal(RuntimeTracePhase.SimUpdate, RuntimeTracePhaseContext.CurrentPhase);
                    throw new InvalidOperationException("deliberate contract-test failure");
                }
                finally
                {
                    scope.Dispose();
                }
            };
            Assert.Throws<InvalidOperationException>(invokeWithException);

            Assert.Equal(RuntimeTracePhase.Unknown, RuntimeTracePhaseContext.CurrentPhase);
        }
    }
}

namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Tests.Fakes;

public class ConcurrencyRetryTests
{
    private readonly CountingUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task RunAsync_RunsOperationAgainFromFreshData_AfterConflict()
    {
        _unitOfWork.ConflictsToSimulate = 2;
        var runs = 0;

        var result = await ConcurrencyRetry.RunAsync(_unitOfWork, async () =>
        {
            runs++;
            await _unitOfWork.SaveChangesAsync();

            return runs;
        });

        Assert.Equal(3, result);
        Assert.Equal(2, _unitOfWork.DiscardCount);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RunAsync_GivesUp_AfterMaxAttempts()
    {
        _unitOfWork.ConflictsToSimulate = ConcurrencyRetry.MaxAttempts;
        var runs = 0;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ConcurrencyRetry.RunAsync(_unitOfWork, async () =>
        {
            runs++;
            await _unitOfWork.SaveChangesAsync();

            return runs;
        }));

        Assert.Equal(ConcurrencyRetry.MaxAttempts, runs);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RunAsync_DoesNotRetry_OtherFailures()
    {
        var runs = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConcurrencyRetry.RunAsync<int>(_unitOfWork, () =>
        {
            runs++;

            throw new InvalidOperationException();
        }));

        Assert.Equal(1, runs);
        Assert.Equal(0, _unitOfWork.DiscardCount);
    }
}

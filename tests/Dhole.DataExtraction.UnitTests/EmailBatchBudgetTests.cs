using Dhole.DataExtraction.Workers.Workers;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailBatchBudgetTests
{
    [TestMethod]
    public void Distribute_DoesNotExceedConfiguredBatch()
    {
        var quotas = EmailBatchBudget.Distribute(32, 4);
        Assert.AreEqual(32, quotas.Sum());
        Assert.HasCount(4, quotas);
        CollectionAssert.AreEquivalent(new[] { 8, 8, 8, 8 }, quotas);
    }

    [TestMethod]
    public void Distribute_NonDivisibleBatch_IsFair()
    {
        var quotas = EmailBatchBudget.Distribute(11, 4);
        Assert.AreEqual(11, quotas.Sum());
        CollectionAssert.AreEquivalent(new[] { 3, 3, 3, 2 }, quotas);
    }

    [TestMethod]
    public void Distribute_SmallerThanSlotCount_DoesNotStartEmptyWorkers()
    {
        var quotas = EmailBatchBudget.Distribute(2, 8);
        CollectionAssert.AreEquivalent(new[] { 1, 1 }, quotas);
    }

    [TestMethod]
    public void Distribute_ClampsUntrustedConfiguration()
    {
        var minimum = EmailBatchBudget.Distribute(-1, 99);
        var maximum = EmailBatchBudget.Distribute(999_999, 999);
        Assert.AreEqual(1, minimum.Sum());
        Assert.HasCount(1, minimum);
        Assert.AreEqual(500, maximum.Sum());
        Assert.HasCount(8, maximum);
    }
}

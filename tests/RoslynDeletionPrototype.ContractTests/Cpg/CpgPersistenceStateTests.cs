using System.Security.Cryptography;
using System.Text;
using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Persistence.Sqlite;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Testing.TestInfrastructure;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

[Collection("CpgPersistenceObserver")]
public sealed class CpgPersistenceStateTests
{
  [Fact]
  public async Task PersistedBuild_AtWriteCheckpoint_StagingIsReadableAndCatalogIsInvisible()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-persistence-state", Guid.NewGuid().ToString("N"));
    var reached = new TaskCompletionSource<CpgShardWriteCheckpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    const string source = "class Example { int Run() => 1; }";
    const string profile = "staging-visibility";
    try
    {
      using var registration = CpgPersistenceTestKit.ObserveShardWrites(checkpoint =>
      {
        if (reached.TrySetResult(checkpoint))
        {
          release.Task.GetAwaiter().GetResult();
        }
      });
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, profile, StreamingMode: true),
      });
      var build = Task.Run(() => builder.BuildFromSource(source, "input.cs"));
      var checkpoint = await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

      Assert.True(Directory.Exists(checkpoint.StagingRoot));
      Assert.False(File.Exists(Path.Combine(checkpoint.StagingRoot, "completed.marker")));
      var stagedStore = new CpgShardStore(checkpoint.StagingRoot);
      var stagedPaths = Directory.EnumerateFiles(checkpoint.StagingRoot, "*.cpgbin", SearchOption.AllDirectories)
        .ToArray();
      Assert.NotEmpty(stagedPaths);
      var staged = await stagedStore.ReadFromPathAsync(stagedPaths[0], CancellationToken.None);
      Assert.NotEmpty(staged.Shard.Nodes);

      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var file = CreateFileKey(source);
      Assert.Empty(await catalog.FindByFileAsync(file, 1, profile, CancellationToken.None));

      release.TrySetResult();
      await build.WaitAsync(TimeSpan.FromSeconds(10));
      Assert.True(File.Exists(Path.Combine(checkpoint.StagingRoot, "completed.marker")));
      Assert.NotEmpty(await catalog.FindByFileAsync(file, 1, profile, CancellationToken.None));
    }
    finally
    {
      release.TrySetResult();
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task PersistedBuild_WriteFailure_CleansStagingAndLeavesCatalogInvisible()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-persistence-state", Guid.NewGuid().ToString("N"));
    const string source = "class Example { int Run() => 1; }";
    const string profile = "write-failure";
    try
    {
      using var registration = CpgPersistenceTestKit.ObserveShardWrites(_ =>
        throw new InvalidOperationException("injected persistence write failure"));
      var builder = CreatePersistedBuilder(root, profile);

      var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
        builder.BuildFromSource(source, "input.cs")));

      Assert.Equal("injected persistence write failure", exception.Message);
      await AssertNoCompletedBuildAsync(root, profile, source);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task PersistedBuild_CancelledWrite_CleansStagingAndLeavesStoreReusable()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-persistence-state", Guid.NewGuid().ToString("N"));
    const string source = "class Example { int Run() => 1; }";
    const string profile = "cancelled-write";
    try
    {
      using var registration = CpgPersistenceTestKit.ObserveShardWrites(_ =>
        throw new OperationCanceledException("injected persistence cancellation"));
      var builder = CreatePersistedBuilder(root, profile);

      var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() =>
        builder.BuildFromSource(source, "input.cs")));

      Assert.Equal("injected persistence cancellation", exception.Message);
      await AssertNoCompletedBuildAsync(root, profile, source);
      using var reopened = await CpgPersistenceTestKit.AcquireStoreLockAsync(
        root,
        TimeSpan.FromSeconds(1),
        CancellationToken.None);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void ObserveShardWrites_PersistedBuild_ReportsTypedCheckpoint()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-persistence-state", Guid.NewGuid().ToString("N"));
    CpgShardWriteCheckpoint? observed = null;
    try
    {
      using var registration = CpgPersistenceTestKit.ObserveShardWrites(checkpoint => observed ??= checkpoint);
      _ = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "typed-checkpoint", StreamingMode: true),
      }).BuildFromSource("class Example { int Run() => 1; }", "input.cs");

      Assert.NotNull(observed);
      Assert.False(string.IsNullOrWhiteSpace(observed!.BuildId));
      Assert.StartsWith(Path.Combine(root, "builds"), observed.StagingRoot, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task AcquireStoreLockAsync_CancelledWait_LeavesStoreReusable()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-persistence-state", Guid.NewGuid().ToString("N"));
    try
    {
      var held = CpgShardStoreLock.Acquire(root);
      using var cancellation = new CancellationTokenSource();
      var waiting = CpgPersistenceTestKit.AcquireStoreLockAsync(root, TimeSpan.FromSeconds(10), cancellation.Token);
      cancellation.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
      held.Dispose();
      using var reopened = await CpgPersistenceTestKit.AcquireStoreLockAsync(root, TimeSpan.FromSeconds(1), CancellationToken.None);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task ObserveShardWrites_ConcurrentBuilds_KeepCallbacksScoped()
  {
    var firstRoot = Path.Combine(Path.GetTempPath(), "cpg-persistence-state", Guid.NewGuid().ToString("N"));
    var secondRoot = Path.Combine(Path.GetTempPath(), "cpg-persistence-state", Guid.NewGuid().ToString("N"));
    var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var firstObserved = new TaskCompletionSource<CpgShardWriteCheckpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondObserved = new TaskCompletionSource<CpgShardWriteCheckpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
    try
    {
      var first = Task.Run(async () =>
      {
        using var registration = CpgPersistenceTestKit.ObserveShardWrites(checkpoint =>
          firstObserved.TrySetResult(checkpoint));
        firstReady.TrySetResult();
        await start.Task;
        _ = CreatePersistedBuilder(firstRoot, "first-observer").BuildFromSource(
          "class First { int Run() => 1; }", "first.cs");
      });
      var second = Task.Run(async () =>
      {
        using var registration = CpgPersistenceTestKit.ObserveShardWrites(checkpoint =>
          secondObserved.TrySetResult(checkpoint));
        secondReady.TrySetResult();
        await start.Task;
        _ = CreatePersistedBuilder(secondRoot, "second-observer").BuildFromSource(
          "class Second { int Run() => 2; }", "second.cs");
      });

      await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(TimeSpan.FromSeconds(10));
      start.TrySetResult();
      await Task.WhenAll(firstObserved.Task, secondObserved.Task).WaitAsync(TimeSpan.FromSeconds(10));
      await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

      Assert.StartsWith(Path.Combine(firstRoot, "builds"), firstObserved.Task.Result.StagingRoot,
        StringComparison.OrdinalIgnoreCase);
      Assert.StartsWith(Path.Combine(secondRoot, "builds"), secondObserved.Task.Result.StagingRoot,
        StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
      if (Directory.Exists(firstRoot))
      {
        Directory.Delete(firstRoot, recursive: true);
      }

      if (Directory.Exists(secondRoot))
      {
        Directory.Delete(secondRoot, recursive: true);
      }
    }
  }

  private static RoslynCpgBuilder CreatePersistedBuilder(string root, string profile)
  {
    return new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
    {
      Persistence = new CpgPersistenceOptions(root, profile, StreamingMode: true),
    });
  }

  private static CpgFileKey CreateFileKey(string source)
  {
    return new CpgFileKey(
      Path.GetFullPath("."),
      "input.cs",
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
  }

  private static async Task AssertNoCompletedBuildAsync(string root, string profile, string source)
  {
    var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
    Assert.Empty(await catalog.FindByFileAsync(CreateFileKey(source), 1, profile, CancellationToken.None));
    Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "builds")));
  }
}

using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using TimeTracker.Core;
using TimeTracker.Desktop;

namespace TimeTracker.Ui.Tests;

/// <summary>Exercises real dispatcher scheduling, locked producer files, and recoverable persistence failures on Windows.</summary>
public sealed class InboxWatcherTests
{
    [Fact]
    public void LockedHeadAndFailedSavesCannotBeOvertakenAndCallbacksStayOnDispatcher()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "TimeTracker.Inbox.Tests", Guid.NewGuid().ToString("N"));
            DispatcherTimer? probe = null;
            FileStream? writer = null;
            try
            {
                var inbox = Path.Combine(directory, "inbox");
                Directory.CreateDirectory(inbox);
                var first = Path.Combine(inbox, "01-first.json");
                var second = Path.Combine(inbox, "02-second.json");
                var firstId = Guid.NewGuid();
                var secondId = Guid.NewGuid();
                writer = new FileStream(first, FileMode.Create, FileAccess.Write, FileShare.None);
                writer.Write(Encoding.UTF8.GetBytes("{"));
                writer.Flush(true);
                File.WriteAllText(second, JsonSerializer.Serialize(new { Command = "end", RowId = secondId }));
                var dispatcher = Dispatcher.CurrentDispatcher;
                var applied = new List<Guid>();
                var attempts = 0;
                using var watcher = new InboxWatcher(directory, dispatcher, command =>
                {
                    Assert.True(dispatcher.CheckAccess());
                    if (command.RowId == firstId && ++attempts < 3) throw new IOException("Simulated unavailable storage");
                    applied.Add(command.RowId);
                });
                var frame = new DispatcherFrame();
                var stopwatch = Stopwatch.StartNew();
                var released = false;
                probe = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal, (_, _) =>
                {
                    try
                    {
                        if (!released && stopwatch.Elapsed > TimeSpan.FromMilliseconds(700))
                        {
                            Assert.Empty(applied);
                            Assert.True(File.Exists(second));
                            writer.SetLength(0);
                            writer.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Command = "end", RowId = firstId })));
                            writer.Flush(true);
                            writer.Dispose();
                            released = true;
                        }
                        if (applied.Count == 2 && !File.Exists(first) && !File.Exists(second)) frame.Continue = false;
                        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Inbox retries did not complete.");
                    }
                    catch (Exception exception) { failure = exception; frame.Continue = false; }
                }, dispatcher);
                Dispatcher.PushFrame(frame);
                Assert.Equal(new[] { firstId, secondId }, applied);
                Assert.Equal(3, attempts);
                Assert.Contains("save deferred", File.ReadAllText(Path.Combine(directory, "time-tracker.log")));
            }
            catch (Exception exception) { failure ??= exception; }
            finally
            {
                probe?.Stop();
                writer?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The inbox dispatcher did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void AnInMemoryCompletionMustBeSavedAgainBeforeAcknowledgmentAfterSaveFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TimeTracker.Inbox.Save.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var model = new MainViewModel(directory);
            model.Add("SAVE-RETRY", TrackerColors.All[0]);
            var path = Path.Combine(directory, "tracker-data.json");
            var before = File.ReadAllText(path);
            var id = model.TodayRows[0].Id;
            var command = new InboxCommand("complete", id, "TC-SAVE", DateTimeOffset.UtcNow);
            using (var externalReader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var exception = Record.Exception(() => model.ApplyInboxCommand(command));
                Assert.True(exception is IOException or UnauthorizedAccessException);
                Assert.Equal(before, File.ReadAllText(path));
                Assert.Equal("", model.Message);
            }
            model.ApplyInboxCommand(command);
            var saved = new JsonFileStore<TrackerDocument>(path, TrackerEngine.Validate).Load(() => TrackerDocument.Empty).Value;
            Assert.Equal("TC-SAVE", Assert.Single(saved.Entries).TimecardId);
            Assert.True(saved.Entries[0].IsArchived && saved.Entries[0].IsEnded);
            Assert.Empty(model.TodayRows);
            Assert.Single(model.EarlierRows);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

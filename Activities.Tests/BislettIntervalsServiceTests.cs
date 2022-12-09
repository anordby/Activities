using System.IO;
using System.Threading.Tasks;
using Activities.Strava.Activities;
using Activities.Strava.Endpoints.Models;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Activities.Tests;

[TestFixture]
public class BislettIntervalsServiceTests
{
    [TestCase(8215675956, true)]
    [TestCase(8215447861, true)]
    [TestCase(8224195215, true)]
    [TestCase(8153844342, true)]
    [TestCase(8132028269, true)]
    [TestCase(8185243869, true)]
    [TestCase(8162696670, false)]
    [TestCase(8122964707, false)]
    [TestCase(8181362347, false)]
    [TestCase(8151972136, false)]
    [TestCase(8130531366, false)]
    [TestCase(7850048611, false)]
    [TestCase(8085782747, false)]
    [TestCase(6371150381, false)]
    [TestCase(3117056036, false)]
    public async Task Detect_interval_laps(long stravaId, bool isBislettInterval)
    {
        var json = await File.ReadAllTextAsync(Path.Combine("BislettActivities", $"{stravaId}.json"));
        var activity = JsonConvert.DeserializeObject<DetailedActivity>(json);
        activity._IntervalVersion = null;
        activity._BislettVersion = null;

        activity.TryTagIntervalLaps();
        activity.TryAdjustBislettLaps();

        Assert.AreEqual(isBislettInterval, activity.IsBislettInterval, stravaId.ToString());
    }
}
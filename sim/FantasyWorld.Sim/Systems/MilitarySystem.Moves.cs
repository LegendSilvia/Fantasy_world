using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The commander's choice of move (sub-project 10 C2). When a host is between moves, its commander chooses the next among
/// the moves open to it: march on towards the goal the orders name (the rules' own pick, weighted highest), lay siege where
/// it stands, hold, strike at an enemy host next door, go round by another neighbour nearer the goal, or fall back towards
/// home (likelier when morale or supply runs low). Claude answers within the story-beat budget; otherwise the rules make
/// the move the orders already name, without a roll. A reasoned choice is told with the move it makes.
/// </summary>
public sealed partial class MilitarySystem
{
    private void ChooseMove(SimContext ctx, Army army)
    {
        var world = ctx.World;
        if (army.DaysToNext > 0 || army.Objective == ArmyObjective.Return) return;
        if (!world.Characters.TryGetValue(army.CommanderId, out var commander) || !commander.IsActive) return;
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var here = world.Regions[army.RegionId];
        var graph = world.Geography!.Graph;
        string Name(long region) => world.Regions[region].Name;
        long? Step(long neighbour) => graph.Between(army.RegionId, neighbour) is { } link && Campaigns.StepDays(world, link, army.RegionId) is not null ? neighbour : null;

        var options = new List<DecisionOption>();
        var goal = army.TargetRegionId;
        var onward = army.Path.Count > 0 ? army.Path[0] : 0;
        if (onward != 0)
            options.Add(new($"advance:{onward}", 6, $"march on towards {Name(goal)}, by way of {Name(onward)}"));
        if (Hostile(world, war, realm, here) && world.LivingSettlements(here).Any())
            options.Add(new("besiege", goal == here.Id ? 6 : 2, $"lay siege to the holdings of {here.Name}, where the host stands"));
        options.Add(new("hold", onward == 0 ? 3 : 1, $"hold {here.Name} and wait for the enemy to move"));
        var enemies = war.EnemiesOf(Side(war, realm));
        foreach (var foe in Active(world).Where(a => a.WarId == war.Id && enemies.Contains(a.RealmId) && a.RegionId != army.RegionId
                                                     && Step(a.RegionId) is not null).OrderBy(a => a.Id).Take(2))
            options.Add(new($"strike:{foe.RegionId}", Strong(world, army) ? 3 : 1, $"strike at {foe.Name} in {Name(foe.RegionId)}"));
        if (goal != 0 && goal != army.RegionId)
        {
            var (toGoal, _) = Campaigns.Roads(world, goal);
            var stay = toGoal.GetValueOrDefault(army.RegionId, double.MaxValue);
            var round = graph.Neighbours(army.RegionId).Where(n => n != onward && Step(n) is not null && toGoal.GetValueOrDefault(n, double.MaxValue) < stay)
                .OrderBy(n => toGoal[n]).ThenBy(n => n).FirstOrDefault();
            if (round != 0) options.Add(new($"round:{round}", 1, $"go round by {Name(round)} towards {Name(goal)}"));
        }
        var home = world.Settlements[realm.CapitalId].RegionId;
        if (home != army.RegionId && Campaigns.Road(world, army.RegionId, home) is { Path.Count: > 1 } back)
            options.Add(new($"fallback:{back.Path[1]}", army.Morale < 40 || army.Supply < 30 ? 4 : 0.5, $"fall back towards {Name(home)}, by way of {Name(back.Path[1])}"));
        if (options.Count < 2) return;

        // The rules' move: on along the road, else the siege the orders name here, else stand.
        var orders = onward != 0 ? $"advance:{onward}" : options.Any(o => o.Key == "besiege") && goal == here.Id ? "besiege" : "hold";
        var choice = _decider.Decide(new DecisionBrief(commander.Id, "campaign", options, orders), ctx.Rng);
        if (choice.Key == orders) return;
        army.Orders = choice.Reasoning;
        var parts = choice.Key.Split(':');
        var to = parts.Length > 1 ? long.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        switch (parts[0])
        {
            case "advance":
                return;
            case "besiege":
                (army.TargetRegionId, army.Objective) = (army.RegionId, ArmyObjective.Siege);
                army.Path.Clear();
                return;
            case "hold":
                (army.TargetRegionId, army.Objective) = (army.RegionId, ArmyObjective.Defend);
                army.Path.Clear();
                return;
            default:
                // A step of the commander's own: to the foe, round, or back; the orders are given again after it.
                army.TargetRegionId = to;
                army.Path.Clear();
                army.Path.Add(to);
                if (parts[0] == "strike") army.Objective = ArmyObjective.Defend;
                return;
        }
    }
}

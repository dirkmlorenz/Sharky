namespace Sharky.Builds.MacroServices
{
    public class WorkerBuilderService
    {
        ActiveUnitData ActiveUnitData;
        SharkyUnitData SharkyUnitData;

        public WorkerBuilderService(DefaultSharkyBot defaultSharkyBot)
        {
            ActiveUnitData = defaultSharkyBot.ActiveUnitData;
            SharkyUnitData = defaultSharkyBot.SharkyUnitData;
        }

        public UnitCommander GetWorker(Point2D location, IEnumerable<UnitCommander> workers = null)
        {
            IEnumerable<UnitCommander> availableWorkers;
            if (workers == null)
            {
                var buildings = ActiveUnitData.SelfUnits.Values.Where(u => u.Attributes.Contains(SC2APIProtocol.Attribute.Structure)).ToArray();
                availableWorkers = ActiveUnitData.Commanders.Values.Where(c => c.UnitCalculation.Unit.UnitType == (uint)UnitTypes.TERRAN_SCV && c.UnitCalculation.Unit.Orders.Any(o => ActiveUnitData.SelfUnits.Values.Any(s => s.Attributes.Contains(SC2APIProtocol.Attribute.Structure) && s.Unit.BuildProgress == 1 && o.TargetWorldSpacePos != null && s.Position.X == o.TargetWorldSpacePos.X && s.Position.Y == o.TargetWorldSpacePos.Y)));
                var workersNotCarrying = ActiveUnitData.Commanders.Values.Where(c => c.UnitCalculation.UnitClassifications.HasFlag(UnitClassification.Worker) && !c.UnitCalculation.Unit.BuffIds.Any(b => SharkyUnitData.CarryingResourceBuffs.Contains((Buffs)b)));
                availableWorkers = availableWorkers.Concat(workersNotCarrying
                    .Where(c => (c.UnitRole == UnitRole.PreBuild || c.UnitRole == UnitRole.None || c.UnitRole == UnitRole.Minerals || c.UnitRole == UnitRole.Build) && !IsBuilding(c)))
                    .OrderBy(p => Vector2.DistanceSquared(p.UnitCalculation.Position, new Vector2(location.X, location.Y)));

                bool IsBuilding(UnitCommander c)
                {
                    var unit = c.UnitCalculation.Unit;
                    var isProbe = unit.UnitType == (uint) UnitTypes.PROTOSS_PROBE;
                    return unit.Orders.Any(IsBuildingOrder);
                    bool HasBuilding(Point l)
                    {
                        return l != null && buildings.Any(b => b.Position.X == l.X && b.Position.Y == l.Y);
                    }
                    bool IsBuildingOrder(UnitOrder o)
                    {
                        if (isProbe && HasBuilding(o.TargetWorldSpacePos))
                        {
                            // if there is already a building at the target position the probe is already available again
                            return false;
                        }
                        return SharkyUnitData.BuildingData.Values.Any(b => (uint)b.Ability == o.AbilityId);
                    }
                }
            }
            else
            {
                availableWorkers = workers.Where(c => !c.UnitCalculation.Unit.Orders.Any(o => SharkyUnitData.BuildingData.Values.Any(b => (uint)b.Ability == o.AbilityId))).OrderBy(p => Vector2.DistanceSquared(p.UnitCalculation.Position, new Vector2(location.X, location.Y)));
            }

            if (availableWorkers.Count() == 0)
            {
                return null;
            }
            else
            {
                var closest = availableWorkers.First();
                var pos = closest.UnitCalculation.Position;
                var distanceSquared = Vector2.DistanceSquared(pos, new Vector2(location.X, location.Y));
                if (distanceSquared > 1000)
                {
                    pos = availableWorkers.First().UnitCalculation.Position;

                    if (Vector2.DistanceSquared(new Vector2(pos.X, pos.Y), new Vector2(location.X, location.Y)) > distanceSquared)
                    {
                        return closest;
                    }
                    else
                    {
                        return availableWorkers.First();
                    }
                }
            }
            return availableWorkers.First();
        }
    }
}

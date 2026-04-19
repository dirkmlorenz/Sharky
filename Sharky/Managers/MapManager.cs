namespace Sharky.Managers
{
    public class MapManager : SharkyManager
    {
        public record ConnectedComponentInfo
        {
            public int[,]? ConnectedComponents { get; set; } = null;
            public int NumConnectedComponents { get; set; } = 0;
        }

        private record StructureInfo(int XMin, int XMax, int YMin, int YMax, int[] TouchingComponents)
        {
            public static StructureInfo Create(int xMin, int xMax, int yMin, int yMax, int[,]? components)
            {
                if (components is null)
                {
                    return new StructureInfo(xMin, xMax, yMin, yMax, Array.Empty<int>());
                }
                var comps = components!;
                var nX = comps.GetLength(0);
                var nY = comps.GetLength(1);
                var cellsToCheck = Enumerable.Empty<(int, int)>();
                var startX = Math.Max(0, xMin - 1);
                var endX = Math.Min(nX - 1, xMax + 1);
                var countX = endX - startX + 1;
                var startY = Math.Max(0, yMin - 1);
                var endY = Math.Min(nY - 1, yMax + 1);
                var countY = endY - startY + 1;
                if (xMin > 0)
                {
                    cellsToCheck = cellsToCheck.Concat(Enumerable.Range(startY, countY).Select(y => (startX, y)));
                }
                if (xMax < nX - 1)
                {
                    cellsToCheck = cellsToCheck.Concat(Enumerable.Range(startY, countY).Select(y => (endX, y)));
                }
                if (yMin > 0)
                {
                    cellsToCheck = cellsToCheck.Concat(Enumerable.Range(startX, countX).Select(x => (x, startY)));
                }
                if (yMax < nY - 1)
                {
                    cellsToCheck = cellsToCheck.Concat(Enumerable.Range(startX, countX).Select(x => (x, endY)));
                }
                return new StructureInfo(xMin, xMax, yMin, yMax, cellsToCheck.Select(xy => components[xy.Item1, xy.Item2]).Where(x => x != 0).Distinct().ToArray());
            }
        }

        ActiveUnitData ActiveUnitData;
        MapData MapData;
        SharkyUnitData SharkyUnitData;
        DebugService DebugService;
        WallDataService WallDataService;
        SharkyOptions SharkyOptions;

        private int LastUpdateFrame;
        private readonly int FramesPerUpdate;
        private readonly HashSet<float> UnexpectedMineralSizes = new();
        private readonly Dictionary<UnitTypes, int> EstimatedFootPrintSizes = new();
        private readonly ConnectedComponentInfo CCInfo = new();
        private StructureInfo?[,] StructureInfos { get; set; } = null;

        public bool FullVisionMode { get; set; } = false;
        public bool DoUpdateConnectedComponents { get; set; } = false;
        private bool DidUpdatedConnectedComponentsRecently = false;

        public MapManager(MapData mapData, ActiveUnitData activeUnitData, SharkyOptions sharkyOptions, SharkyUnitData sharkyUnitData, DebugService debugService, WallDataService wallDataService)
        {
            MapData = mapData;
            ActiveUnitData = activeUnitData;
            SharkyUnitData = sharkyUnitData;
            DebugService = debugService;
            WallDataService = wallDataService;
            SharkyOptions = sharkyOptions;

            FramesPerUpdate = 5;
            LastUpdateFrame = -100;
        }

        public override void OnStart(ResponseGameInfo gameInfo, ResponseData data, ResponsePing pingResponse, ResponseObservation observation, uint playerId, string opponentId)
        {
            var placementGrid = gameInfo.StartRaw.PlacementGrid;
            var heightGrid = gameInfo.StartRaw.TerrainHeight;
            var pathingGrid = gameInfo.StartRaw.PathingGrid;
            MapData.MapWidth = pathingGrid.Size.X;
            MapData.MapHeight = pathingGrid.Size.Y;
            MapData.Map = new MapCell[MapData.MapWidth, MapData.MapHeight];
            for (var x = 0; x < pathingGrid.Size.X; x++)
            {
                for (var y = 0; y < pathingGrid.Size.Y; y++)
                {
                    var walkable = GetDataValueBit(pathingGrid, x, y);
                    var height = GetDataValueByte(heightGrid, x, y);
                    var placeable = GetDataValueBit(placementGrid, x, y);
                    MapData.Map[x,y] = new MapCell { X = x, Y = y, Walkable = walkable, TerrainHeight = height, Buildable = placeable, HasCreep = false, CurrentlyBuildable = placeable, EnemyAirDpsInRange = 0, EnemyGroundDpsInRange = 0, InEnemyVision = false, InSelfVision = false, InEnemyDetection = false, InSelfDetection = false, Visibility = 0, LastFrameVisibility = 0, NumberOfAllies = 0, NumberOfEnemies = 0, PoweredBySelfPylon = false, SelfAirDpsInRange = 0, SelfGroundDpsInRange = 0, LastFrameAlliesTouched = 0, PathBlocked = false };
                }
            }

           MapData.MapName = gameInfo.MapName;
        }

        public override IEnumerable<SC2APIProtocol.Action> OnFrame(ResponseObservation observation)
        {
            if (SharkyOptions.DrawGrid)
            {
                DrawGrid(observation.Observation.RawData.Player.Camera);
            }
            //DrawPaths();

            if (FramesPerUpdate > observation.Observation.GameLoop - LastUpdateFrame) { return null; }
            LastUpdateFrame = (int)observation.Observation.GameLoop;

            UpdateVisibility(observation.Observation.RawData.MapState.Visibility, (int)observation.Observation.GameLoop);
            UpdateCreep(observation.Observation.RawData.MapState.Creep);
            UpdateEnemyDpsInRange();
            UpdateInEnemyDetection();
            UpdateInSelfDetection();
            UpdateInEnemyVision();
            UpdateNumberOfAllies((int)observation.Observation.GameLoop);
            UpdatePathBlocked();
            if (DoUpdateConnectedComponents)
            {
                if (DidUpdatedConnectedComponentsRecently)
                {
                    DidUpdatedConnectedComponentsRecently = false;
                }
                else
                {
                    UpdateConnectedComponents();
                    UpdateStructureInfos();
                    DidUpdatedConnectedComponentsRecently = true;
                }
            }

            return null;
        }

        private void DrawPaths()
        {
            var height = 12;
            var color = new Color { R = 255, G = 255, B = 255 };

            foreach (var path in MapData.PathData.Skip(50).Take(100))
            {
                DebugService.DrawSphere(new Point { X = path.StartPosition.X, Y = path.StartPosition.Y, Z = height }, .25f, new Color { R = 1, G = 255, B = 1 });
                DebugService.DrawSphere(new Point { X = path.EndPosition.X, Y = path.EndPosition.Y, Z = height }, .25f, new Color { R = 255, G = 1, B = 1 });
                var previousPoint = new Point { X = path.StartPosition.X, Y = path.StartPosition.Y, Z = height };
                foreach (var vector in path.Path)
                {
                    var point = new Point { X = vector.X, Y = vector.Y, Z = height };
                    DebugService.DrawLine(previousPoint, point, color);
                    DebugService.DrawLine(point, new Point { X = point.X, Y = point.Y, Z = 1 }, color);

                    previousPoint = point;
                }
            }
        }

        private void DrawGrid(Point camera)
        {
            var height = 12;

            DebugService.DrawText($"Point: {camera.X},{camera.Y}");
            DebugService.DrawSphere(new Point { X = camera.X, Y = camera.Y, Z = height }, .25f);
            DebugService.DrawLine(new Point { X = camera.X, Y = camera.Y, Z = height }, new Point { X = camera.X, Y = camera.Y, Z = 0 }, new Color { R = 255, G = 255, B = 255 });

            for (int x = -5; x <= 5; x++)
            {
                for (int y = -5; y <= 5; y++)
                {
                    var point = new Point { X = (int)camera.X + x, Y = (int)camera.Y + y, Z = height + 1 };
                    var color = new Color { R = 255, G = 255, B = 255 };
                    if (point.X + 1 < MapData.MapWidth && point.Y + 1 < MapData.MapHeight && point.X > 0 && point.Y > 0)
                    {        
                        if (!MapData.Map[(int)point.X,(int)point.Y].CurrentlyBuildable)
                        {
                            color = new Color { R = 255, G = 0, B = 0 };
                        }
                        DebugService.DrawLine(point, new Point { X = point.X + 1, Y = point.Y, Z = height + 1 }, color);
                        DebugService.DrawLine(point, new Point { X = point.X, Y = point.Y + 1, Z = height + 1 }, color);
                        DebugService.DrawLine(point, new Point { X = point.X, Y = point.Y + 1, Z = 1 }, color);
                    }
                }
            }
        }

        void UpdateNumberOfAllies(int frame)
        {
            for (var x = 0; x < MapData.MapWidth; x++)
            {
                for (var y = 0; y < MapData.MapHeight; y++)
                {
                    MapData.Map[x,y].NumberOfAllies = 0;
                }
            }

            foreach (var selfUnit in ActiveUnitData.SelfUnits)
            {
                var nodes = GetNodesInRange(selfUnit.Value.Unit.Pos, selfUnit.Value.Unit.Radius, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].NumberOfAllies += 1;
                    MapData.Map[(int)node.X,(int)node.Y].LastFrameAlliesTouched = frame;
                }
            }
        }

        void UpdateEnemyDpsInRange()
        {
            for (var x = 0; x < MapData.MapWidth; x++)
            {
                for (var y = 0; y < MapData.MapHeight; y++)
                {
                    var mc = MapData.Map[x,y];
                    mc.EnemyAirDpsInRange = 0;
                    mc.EnemyGroundDpsInRange = 0;
                    mc.EnemyAirSplashDpsInRange = 0;
                    mc.EnemyGroundSplashDpsInRange = 0;
                }
            }

            foreach (var enemy in ActiveUnitData.EnemyUnits.Where(e => e.Value.Unit.BuildProgress == 1 && !e.Value.Unit.BuffIds.Contains((uint)Buffs.ORACLESTASISTRAPTARGET)))
            {
                if (enemy.Value.DamageAir)
                {
                    var nodes = GetNodesInRange(enemy.Value.Unit.Pos, enemy.Value.Range + 2, MapData.MapWidth, MapData.MapHeight);
                    var splash = SharkyUnitData.AirSplashDamagers.Contains((UnitTypes)enemy.Value.Unit.UnitType);
                    foreach (var node in nodes)
                    {
                        MapData.Map[(int)node.X,(int)node.Y].EnemyAirDpsInRange += enemy.Value.Dps;
                        if (splash)
                        {
                            MapData.Map[(int)node.X,(int)node.Y].EnemyAirSplashDpsInRange += enemy.Value.Dps;
                        }
                    }
                }
                if (enemy.Value.DamageGround)
                {
                    var nodes = GetNodesInRange(enemy.Value.Unit.Pos, enemy.Value.Range + 2, MapData.MapWidth, MapData.MapHeight);
                    var splash = SharkyUnitData.GroundSplashDamagers.Contains((UnitTypes)enemy.Value.Unit.UnitType);
                    foreach (var node in nodes)
                    {
                        MapData.Map[(int)node.X,(int)node.Y].EnemyGroundDpsInRange += enemy.Value.Dps;
                        if (splash)
                        {
                            MapData.Map[(int)node.X,(int)node.Y].EnemyGroundSplashDpsInRange += enemy.Value.Dps;
                        }
                    }
                }
                if (enemy.Value.Unit.UnitType == (uint)UnitTypes.ZERG_INFESTOR || enemy.Value.Unit.UnitType == (uint)UnitTypes.PROTOSS_HIGHTEMPLAR)
                {                
                    if (enemy.Value.Unit.Energy > 70)
                    {
                        var nodes = GetNodesInRange(enemy.Value.Unit.Pos, 12, MapData.MapWidth, MapData.MapHeight);
                        foreach (var node in nodes)
                        {
                            MapData.Map[(int)node.X,(int)node.Y].EnemyAirSplashDpsInRange += 50;
                            MapData.Map[(int)node.X,(int)node.Y].EnemyGroundSplashDpsInRange += 50;
                        }
                    }
                }
            }
        }

        private bool BlocksPath(UnitCalculation uc) => !uc.Unit.IsFlying && uc.Attributes.Contains(SC2APIProtocol.Attribute.Structure) && uc.Unit.UnitType != (uint)UnitTypes.TERRAN_SUPPLYDEPOTLOWERED;

        private IEnumerable<UnitCalculation> PathBlockers() => ActiveUnitData.EnemyUnits.Values.Concat(ActiveUnitData.SelfUnits.Values).Concat(ActiveUnitData.NeutralUnits.Values).Where(BlocksPath);

        void UpdatePathBlocked()
        {
            for (var x = 0; x < MapData.MapWidth; x++)
            {
                for (var y = 0; y < MapData.MapHeight; y++)
                {
                    MapData.Map[x,y].PathBlocked = false;
                }
            }

            foreach (var uc in PathBlockers())
            {
                var nodes = GetNodesInFootPrint(uc.Unit, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].PathBlocked = true;
                }
            }
        }

        void UpdateInSelfDetection()
        {
            for (var x = 0; x < MapData.MapWidth; x++)
            {
                for (var y = 0; y < MapData.MapHeight; y++)
                {
                    MapData.Map[x,y].InSelfDetection = false;
                }
            }

            foreach (var unitCalculation in ActiveUnitData.SelfUnits.Where(e => e.Value.UnitClassifications.HasFlag(UnitClassification.Detector) && e.Value.Unit.BuildProgress == 1))
            {
                var nodes = GetNodesInRange(unitCalculation.Value.Unit.Pos, unitCalculation.Value.Unit.DetectRange + 1, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].InSelfDetection = true;
                }
            }

            foreach (var scan in SharkyUnitData.Effects.Where(e => e.EffectId == (uint)Effects.SCAN && e.Alliance == Alliance.Self))
            {
                var nodes = GetNodesInRange(new Point { X = scan.Pos[0].X, Y = scan.Pos[0].Y, Z = 1 }, scan.Radius + 2, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].InSelfDetection = true;
                }
            }
        }

        void UpdateInEnemyDetection()
        {
            for (var x = 0; x < MapData.MapWidth; x++)
            {
                for (var y = 0; y < MapData.MapHeight; y++)
                {
                    MapData.Map[x,y].InEnemyDetection = false;
                }
            }

            foreach (var enemy in ActiveUnitData.EnemyUnits.Where(e => e.Value.UnitClassifications.HasFlag(UnitClassification.Detector) && (e.Value.Unit.BuildProgress == 1 || e.Value.Unit.BuildProgress == 0)))
            {
                var nodes = GetNodesInRange(enemy.Value.Unit.Pos, 11, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].InEnemyDetection = true;
                }
            }

            foreach (var scan in SharkyUnitData.Effects.Where(e => e.EffectId == (uint)Effects.SCAN && e.Alliance == Alliance.Enemy))
            {
                var nodes = GetNodesInRange(new Point { X = scan.Pos[0].X, Y = scan.Pos[0].Y, Z = 1 }, scan.Radius + 2, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].InEnemyDetection = true;
                }
            }
        }

        void UpdateInEnemyVision()
        {
            if (FullVisionMode)
            {
                for (var x = 0; x < MapData.MapWidth; x++)
                {
                    for (var y = 0; y < MapData.MapHeight; y++)
                    {
                        MapData.Map[x, y].InEnemyVision = true;
                    }
                }
                return;
            }

            for (var x = 0; x < MapData.MapWidth; x++)
            {
                for (var y = 0; y < MapData.MapHeight; y++)
                {
                    MapData.Map[x,y].InEnemyVision = false;
                }
            }

            foreach (var enemy in ActiveUnitData.EnemyUnits)
            {
                var radius = 12;
                if (enemy.Value.Unit.BuildProgress < 1)
                {
                    radius = 6;
                }
                var nodes = GetNodesInRange(enemy.Value.Unit.Pos, radius, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].InEnemyVision = true;
                }
            }

            foreach (var scan in SharkyUnitData.Effects.Where(e => e.EffectId == (uint)Effects.SCAN && e.Alliance == Alliance.Enemy))
            {
                var nodes = GetNodesInRange(new Point { X = scan.Pos[0].X, Y = scan.Pos[0].Y, Z = 1 }, scan.Radius + 2, MapData.MapWidth, MapData.MapHeight);
                foreach (var node in nodes)
                {
                    MapData.Map[(int)node.X,(int)node.Y].InEnemyVision = true;
                }
            }
        }

        private List<Vector2> GetNodesInRange(Point position, float range, int columns, int rows)
        {
            var nodes = new List<Vector2>();
            var xMin = (int)Math.Floor(position.X - range);
            var xMax = (int)Math.Ceiling(position.X + range);
            int yMin = (int)Math.Floor(position.Y - range);
            int yMax = (int)Math.Ceiling(position.Y + range);

            if (xMin < 0)
            {
                xMin = 0;
            }
            if (xMax >= columns)
            {
                xMax = columns - 1;
            }
            if (yMin < 0)
            {
                yMin = 0;
            }
            if (yMax >= rows)
            {
                yMax = rows - 1;
            }

            for (int x = xMin; x <= xMax; x++)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    nodes.Add(new Vector2(x, y));
                }
            }

            return nodes;
        }

        private (int xMin, int xMax, int yMin, int yMax) GetFootPrintNodeRange(Unit unit)
        {
            /// In the SC2APIProtocol, the unit radius (how big it is for combat/collisions) is different from its footprint (the square grid cells it occupies on the map).
            /// To get the footprint, you don't look at the Unit itself; you look at the UnitTypeData inside the ResponseData (usually requested once at the start of the game via RequestData).
            /// 1. The footprint Field
            /// Each UnitTypeData message contains a footprint field (string). This string refers to a specific shape defined in the game's balance files (e.g., Footprint3x3, Footprint4x4Contour).
            /// Town Halls (Nexus/CC/Hatchery): Usually a 5x5 footprint.
            /// Supply Depots/Pylons: Usually a 2x2 footprint.
            /// Barracks/Gateways: Usually a 3x3 footprint.
            /// Mineral Patches: Usually a 2x1 (horizontal) footprint.
            /// 2. Converting Footprint to Grid Cells
            /// Since footprints are usually square or rectangular, you don't use a "radius" (which is circular). Instead, you use the center coordinates of the unit and "block out" the grid cells:
            /// If the footprint is Odd (3x3, 5x5): The unit's pos (x, y) will be on an integer (e.g., 10.0, 20.0). You block out the cells from pos.x - 1 to pos.x + 1 for a 3x3.
            /// If the footprint is Even (2x2, 4x4): The unit's pos (x, y) will be on a half-integer (e.g., 10.5, 20.5). You block out the cells by rounding down/up.
            /// 3. Quick Reference Table
            /// Unit Type	Footprint Size	Radius (Combat)
            /// Town Hall	5x5	2.75
            /// Mineral Patch	2x1	1.125
            /// Vespene Geyser	3x3	1.75
            /// Supply Depot	2x2	1.0
            /// Barracks	3x3	1.5            

            var position = unit.Pos;
            var radius = unit.Radius;
            var ut = (UnitTypes)unit.UnitType;
            SharkyUnitData.BuildingData.TryGetValue(ut, out var buildingTypeData);

            if (buildingTypeData is not null)
            {
                return NByN(buildingTypeData.Size);
            }
            if (SharkyUnitData.GasGeyserTypes.Contains(ut) || SharkyUnitData.GasGeyserRefineryTypes.Contains(ut))
            {
                return NByN(3);
            }
            if (SharkyUnitData.MineralFieldTypes.Contains(ut))
            {
                if (radius != 1.125)
                {
                    if (!UnexpectedMineralSizes.Contains(radius))
                    {
                        Console.WriteLine($"MapManager: Unexpectedly sized mineral patch with radius of {radius}");
                        UnexpectedMineralSizes.Add(radius);
                    }
                }
                // we assume that mineral fields are horizontal - in principle they can be rotated
                var x = (int)Math.Round(position.X);
                var y = (int)Math.Round(position.Y - 0.5);
                return (x - 1, x, y, y);
            }
            if (radius == 1.125) // e.g. unbuildable plates
            {
                return NByN(2);
            }
            if (radius == 1.8125) // e.g. warp gate
            {
                return NByN(3);
            }
            if (radius == 2.75)
            {
                return NByN(5);
            }
            if (radius == 3.1875) // e.g. large destructible rocks or tentacle rocks
            {
                return NByN(6);
            }
            if (!EstimatedFootPrintSizes.TryGetValue(ut, out int estimatedFootPrintSize))
            {
                estimatedFootPrintSize = Math.Max(1, (int)Math.Floor(2 * radius));
                EstimatedFootPrintSizes[ut] = estimatedFootPrintSize;
                Console.WriteLine($"Map Manager: Estimated foot print size for structure {unit} with radius {radius} as {estimatedFootPrintSize}");
            }
            return NByN(estimatedFootPrintSize);

            (int, int, int, int) NByN(int n)
            {
                var steps = Math.DivRem(n, 2, out var remainder);
                var centerOffSet = remainder == 0 ? 0f : 0.5f;
                var x = (int)Math.Round(position.X - centerOffSet);
                var y = (int)Math.Round(position.Y - centerOffSet);
                return (x - steps, x + steps + remainder - 1, y - steps, y + steps + remainder - 1);
            }
        }

        private List<Vector2> GetNodesInFootPrint(Unit unit, int columns, int rows)
        {
            var nodes = new List<Vector2>();
            var (xMin, xMax, yMin, yMax) = GetFootPrintNodeRange(unit);

            if (xMin < 0)
            {
                xMin = 0;
            }
            if (xMax >= columns)
            {
                xMax = columns - 1;
            }
            if (yMin < 0)
            {
                yMin = 0;
            }
            if (yMax >= rows)
            {
                yMax = rows - 1;
            }

            for (int x = xMin; x <= xMax; x++)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    nodes.Add(new Vector2(x, y));
                }
            }

            return nodes;
        }

        void UpdateCreep(ImageData creep)
        {
            for (var x = 0; x < creep.Size.X; x++)
            {
                for (var y = 0; y < creep.Size.Y; y++)
                {
                    MapData.Map[x,y].HasCreep = GetDataValueBit(creep, x, y);
                }
            }
        }

        void UpdateVisibility(ImageData visiblilityMap, int frame)
        {
            if (FullVisionMode)
            {
                for (var x = 0; x < visiblilityMap.Size.X; x++)
                {
                    for (var y = 0; y < visiblilityMap.Size.Y; y++)
                    {
                        var cell = MapData.Map[x, y];
                        cell.InSelfVision = true;
                        cell.Visibility = 2;// 2 is fully visible
                        cell.LastFrameVisibility = frame;
                    }
                }
                return;
            }

            for (var x = 0; x < visiblilityMap.Size.X; x++)
            {
                for (var y = 0; y < visiblilityMap.Size.Y; y++)
                {
                    var cell = MapData.Map[x, y];
                    var visibility = GetDataValueByte(visiblilityMap, x, y);
                    var fullyVisible = visibility == 2; // 2 is fully visible
                    cell.InSelfVision = fullyVisible;
                    cell.Visibility = visibility;
                    if (fullyVisible)
                    {
                        cell.LastFrameVisibility = frame;
                    }
                }
            }
        }

        bool GetDataValueBit(ImageData data, int x, int y)
        {
            int pixelID = x + y * data.Size.X;
            int byteLocation = pixelID / 8;
            int bitLocation = pixelID % 8;
            return ((data.Data[byteLocation] & 1 << (7 - bitLocation)) == 0) ? false : true;
        }
        int GetDataValueByte(ImageData data, int x, int y)
        {
            int pixelID = x + y * data.Size.X;
            return data.Data[pixelID];
        }

        private static bool IsWalkable(MapCell[,] map, int x, int y)
        {
            var cell = map[x, y];
            return cell.Walkable && !cell.PathBlocked;
        }

        private (int x, int y) NearbyWalkable(MapCell[,] map, int x, int y)
        {
            if (IsWalkable(map, x, y))
            {
                return (x, y);
            }
            var xMax = map.GetLength(0) - 1;
            var yMax = map.GetLength(1) - 1;
            for (int i = 1; i < 10;  i++)
            {
                var xl = Math.Max(0, x - i);
                var yl = Math.Max(0, y - i);
                if (IsWalkable(map, xl, yl))
                {
                    return (xl, yl);
                }
                var xu = Math.Min(xMax, x + i);
                if (IsWalkable(map, xu, yl))
                {
                    return (xu, yl);
                }
                var yu = Math.Min(yMax, y + i);
                if (IsWalkable(map, xl, yu))
                {
                    return (xl, yu);
                }
                if (IsWalkable(map, xu, yu))
                {
                    return (xu, yu);
                }
            }
            return (x, y);
        }

        private static List<(int x, int y)> ReconstructPath(
            (int x, int y)?[,] parent,
            (int x, int y) start,
            (int x, int y) end,
            bool reverse = false
        )
        {
            var path = new List<(int x, int y)>();
            var current = end;

            while (current != start)
            {
                path.Add(current);
                current = parent[current.x, current.y].Value;
            }

            path.Add(start);
            if (!reverse)
            {
                path.Reverse();
            }
            return path;
        }

        public List<(int x, int y)> FindShortestWalkablePath((int x, int y) start, (int x, int y) end, bool reverse = false)
        {
            var map = MapData.Map;
            var rows = MapData.MapWidth;
            var cols = MapData.MapHeight;

            start = NearbyWalkable(map, start.x, start.y);
            end = NearbyWalkable(map, end.x, end.y);

            var directions = new (int dx, int dy)[]
            {
                (0, 1), (1, 0), (0, -1), (-1, 0), (-1, -1), (-1, 1), (1, -1), (1, 1)
            };

            var queue = new Queue<(int x, int y)>();
            var visited = new bool[rows, cols];
            var parent = new (int x, int y)?[rows, cols];

            queue.Enqueue(start);
            visited[start.x, start.y] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (current == end)
                    return ReconstructPath(parent, start, end, reverse);

                foreach (var (dx, dy) in directions)
                {
                    int nx = current.x + dx;
                    int ny = current.y + dy;

                    if (nx >= 0 && ny >= 0 && nx < rows && ny < cols)
                    {
                        if (!visited[nx, ny] && IsWalkable(map, nx, ny))
                        {
                            visited[nx, ny] = true;
                            parent[nx, ny] = current;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }

            // No path found
            return null;
        }

        public List<(int x, int y)> FindShortestWalkablePath((float x, float y) start, (float x, float y) end, bool reverse = false) => FindShortestWalkablePath(((int)start.x, (int)start.y), ((int)end.x, (int)end.y), reverse);

        private void UpdateConnectedComponents()
        {
            var map = MapData.Map;
            var nX = MapData.MapWidth;
            var nY = MapData.MapHeight;
            CCInfo.ConnectedComponents ??= new int[nX, nY];
            var components = CCInfo.ConnectedComponents!;
            Array.Clear(components);
            var component = 0;
            var dr = new int[] { -1, 1, 0, 0, -1, -1, 1, 1 };
            var dc = new int[] { 0, 0, -1, 1, -1, 1, -1, 1 };

            for (int i = 0; i < nX; i++)
            {
                for (int j = 0; j < nY; j++)
                {
                    if (!IsWalkable(map, i, j) || components[i, j] != 0)
                        continue;

                    component++;

                    Queue<(int r, int c)> queue = new();
                    queue.Enqueue((i, j));
                    components[i, j] = component;

                    while (queue.Count > 0)
                    {
                        var (cr, cc) = queue.Dequeue();

                        for (int n = 0; n < 4; n++)
                        {
                            var nr = cr + dr[n];
                            var nc = cc + dc[n];

                            if (nr < 0 || nc < 0 || nr >= nX || nc >= nY)
                                continue;

                            if (!IsWalkable(map, nr, nc) || components[nr, nc] != 0)
                                continue;

                            components[nr, nc] = component;
                            queue.Enqueue((nr, nc));
                        }
                    }
                }
            }
            if (component != CCInfo.NumConnectedComponents)
            {
                if (CCInfo.NumConnectedComponents != 0)
                {
                    //TagService.Tag($"ncc_{NumConnectedComponents}_{component}");
                }
                CCInfo.NumConnectedComponents = component;
            }
        }

        private void UpdateStructureInfos()
        {
            if (CCInfo.ConnectedComponents is null)
            {
                return;
            }
            var nX = MapData.MapWidth;
            var nY = MapData.MapHeight;
            StructureInfos ??= new StructureInfo[nX, nY];
            Array.Clear(StructureInfos);
            var sis = StructureInfos!;
            var components = CCInfo.ConnectedComponents!;
            foreach (var uc in PathBlockers())
            {
                var (xMin, xMax, yMin, yMax) = GetFootPrintNodeRange(uc.Unit);
                var structureInfo = StructureInfo.Create(xMin, xMax, yMin, yMax, components);
                xMin = Math.Max(0, xMin);
                xMax = Math.Min(nX - 1, xMax);
                yMin = Math.Max(0, yMin);
                yMax = Math.Min(nY  - 1, yMax);
                for (int x = xMin; x <= xMax; x++)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        sis[x, y] = structureInfo;
                    }
                }
            }
        }

        public ConnectedComponentInfo GetConnectedComponentInfo()
        {
            return CCInfo;
        }

        public int[] GetConnectedComponents(int x, int y)
        {
            if (CCInfo.ConnectedComponents is null || x < 0 || y < 0 || x >= MapData.MapWidth || y >= MapData.MapHeight)
            {
                return Array.Empty<int>();
            }
            var component = CCInfo.ConnectedComponents[x, y];
            if (component != 0)
            {
                return new int[] { component };
            }
            if (StructureInfos is null)
            {
                return Array.Empty<int>();
            }
            var structureInfo = StructureInfos[x, y];
            return structureInfo is null ? Array.Empty<int>() : structureInfo.TouchingComponents;
        }

        public int[] GetConnectedComponents(float x, float y) => GetConnectedComponents((int)x, (int)y);
        public int[] GetConnectedComponents(Vector2 pos) => GetConnectedComponents(pos.X, pos.Y);
        public int[] GetConnectedComponents(UnitCalculation uc) => GetConnectedComponents(uc.Position);
        public int[] GetConnectedComponents(SC2APIProtocol.Point p) => GetConnectedComponents(p.X, p.Y);
        public int[] GetConnectedComponents(SC2APIProtocol.Point2D p) => GetConnectedComponents(p.X, p.Y);

        public int[] GetConnectedComponentsByUnitTag(ulong  unitTag)
        {
            if (ActiveUnitData.NeutralUnits.TryGetValue(unitTag, out var n))
            {
                return GetConnectedComponents(n);
            }
            if (ActiveUnitData.EnemyUnits.TryGetValue(unitTag, out var e))
            {
                return GetConnectedComponents(e);
            }
            if (ActiveUnitData.SelfUnits.TryGetValue(unitTag, out var s))
            {
                return GetConnectedComponents(s);
            }
            return Array.Empty<int>();
        }

        public bool IsCrossingComponents(UnitCalculation uc)
        {
            var orders = uc.Unit.Orders;
            if (orders.Count == 0)
            {
                return false;
            }
            var components = GetConnectedComponents(uc);
            if (components.Length != 1)
            {
                return false;
            }
            var component = components[0];
            foreach (var o in uc.Unit.Orders)
            {
                var targetComponents = o.TargetCase switch
                {
                    UnitOrder.TargetOneofCase.TargetWorldSpacePos => GetConnectedComponents(o.TargetWorldSpacePos),
                    UnitOrder.TargetOneofCase.TargetUnitTag => GetConnectedComponentsByUnitTag(o.TargetUnitTag),
                    _ => Array.Empty<int>()
                };
                return targetComponents.Length > 0 && !targetComponents.Contains(component);
            }
            return false;
        }
    }
}

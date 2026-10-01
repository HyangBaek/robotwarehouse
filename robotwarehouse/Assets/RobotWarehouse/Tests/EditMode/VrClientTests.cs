using System.Collections.Generic;
using NUnit.Framework;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using RobotWarehouse.Heatmap;
using RobotWarehouse.InputModule;
using RobotWarehouse.Playback;
using RobotWarehouse.Warehouse;
using UnityEngine;

namespace RobotWarehouse.Tests
{
    /// <summary>개발자 테스트 시나리오 2.6 Unity 자동 테스트 (UN). Window > General > Test Runner > EditMode.</summary>
    public class VrClientTests
    {
        static GridMap LoadW2()
        {
            var asset = Resources.Load<TextAsset>("Offline/w2_map");
            Assert.IsNotNull(asset, "Resources/Offline/w2_map.json 필요");
            var root = Newtonsoft.Json.Linq.JToken.Parse(asset.text);
            return GridMap.FromToken(root["map"] ?? root);
        }

        [Test] // TC-VR-01
        public void GridToUnity_OriginAndAxes()
        {
            Assert.AreEqual(new Vector3(0, 0, 0), GridCoord.CellToLocal(0, 0));
            Assert.AreEqual(new Vector3(29, 0, 19), GridCoord.CellToLocal(29, 19));
            Assert.AreEqual(new Vector2Int(29, 19), GridCoord.LocalToCell(new Vector3(29.4f, 0, 18.6f)));
            Assert.AreEqual(new Vector3(58, 0, 38), GridCoord.CellToLocal(29, 19, 2f));
        }

        [Test] // TC-VR-02
        public void Renderer_BlockCountsMatchJson()
        {
            var map = LoadW2();
            var go = new GameObject("TestWarehouse");
            try
            {
                var r = go.AddComponent<WarehouseRenderer>();
                r.Build(map);
                int jsonRacks = 0, jsonWalls = 0;
                foreach (var c in map.cells)
                {
                    if (c.type == "rack") jsonRacks++;
                    if (c.type == "wall") jsonWalls++;
                }
                Assert.AreEqual(jsonRacks, r.RackCount);
                Assert.AreEqual(jsonWalls, r.WallCount);
                Assert.AreEqual(jsonRacks, map.CountCells(CellType.Rack));
                // 칸 좌표 → 월드 좌표 일치 (TC-VR-13 자동화 버전)
                foreach (var kv in r.RackObjects)
                {
                    var p = kv.Value.transform.localPosition;
                    Assert.AreEqual(kv.Key.x, Mathf.RoundToInt(p.x));
                    Assert.AreEqual(kv.Key.y, Mathf.RoundToInt(p.z));
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        static List<SimFrame> TwoFrames()
        {
            return new List<SimFrame>
            {
                new SimFrame { t = 10, robots = new List<RobotState> { new RobotState { id = "R1", x = 2, y = 3, state = "move" } } },
                new SimFrame { t = 11, robots = new List<RobotState> { new RobotState { id = "R1", x = 3, y = 3, state = "move" } } },
            };
        }

        [Test] // TC-VR-03
        public void Timeline_InterpolatesHalfway()
        {
            var tl = new FrameTimeline();
            tl.Reset(11);
            tl.AddFrames(TwoFrames());
            Assert.IsTrue(tl.TryGetRobot("R1", 10.5f, out var pos, out _));
            Assert.AreEqual(2.5f, pos.x, 1e-4);
            Assert.AreEqual(3f, pos.y, 1e-4);
        }

        [Test] // TC-VR-04
        public void Timeline_Speed4xAdvancesFourTimes()
        {
            var frames = new List<SimFrame>();
            for (int t = 0; t <= 100; t++)
                frames.Add(new SimFrame { t = t, robots = new List<RobotState> { new RobotState { id = "R1", x = t, y = 0 } } });

            float Run(float speed)
            {
                var tl = new FrameTimeline { StepsPerSecond = 2f, Speed = speed };
                tl.Reset(100);
                tl.AddFrames(frames);
                tl.Playing = true;
                for (int i = 0; i < 10; i++) tl.Advance(0.1f);   // 1초
                return tl.Time;
            }
            Assert.AreEqual(Run(1f) * 4f, Run(4f), 1e-3);
        }

        [Test] // SC-05 버퍼링: 받지 않은 프레임 너머로 가지 않는다
        public void Timeline_StopsAtLoadedFrames()
        {
            var tl = new FrameTimeline { StepsPerSecond = 10f };
            tl.Reset(500);
            tl.AddFrames(TwoFrames());   // t=10,11만 있음 → 0부터 연속 아님
            tl.Playing = true;
            tl.Advance(1f);
            Assert.AreEqual(0f, tl.Time);
            Assert.IsTrue(tl.IsBuffering);
        }

        [Test] // SC-08 롤링 재계획
        public void Timeline_DropFromKeepsPrefix()
        {
            var tl = new FrameTimeline();
            var frames = new List<SimFrame>();
            for (int t = 0; t <= 20; t++) frames.Add(new SimFrame { t = t });
            tl.Reset(20);
            tl.AddFrames(frames);
            tl.DropFrom(10);
            Assert.AreEqual(9, tl.LoadedUntil);
            Assert.IsTrue(tl.TryGetFrame(9, out _));
            Assert.IsFalse(tl.TryGetFrame(10, out _));
        }

        [Test] // TC-VR-05
        public void Heatmap_NormalizesAndDarkensWithValue()
        {
            var stats = new List<CellStat>
            {
                new CellStat { x = 0, y = 0, wait = 0 },
                new CellStat { x = 1, y = 0, wait = 5 },
                new CellStat { x = 2, y = 0, wait = 10 },
            };
            var n = HeatmapMath.Normalize(stats, HeatmapMetric.Wait);
            Assert.AreEqual(0f, n[new Vector2Int(0, 0)]);
            Assert.AreEqual(0.5f, n[new Vector2Int(1, 0)], 1e-4);
            Assert.AreEqual(1f, n[new Vector2Int(2, 0)]);
            var mid = HeatmapMath.ToColor(0.5f);
            var max = HeatmapMath.ToColor(1f);
            Assert.Greater(max.a, mid.a);
            Assert.Less(max.g, mid.g);   // 값이 클수록 진한 빨강
            Assert.AreEqual(0f, HeatmapMath.ToColor(0f).a);
            Assert.AreEqual(2, HeatmapMath.Top(stats, HeatmapMetric.Wait, 1)[0].x);
        }

        [Test] // TC-VR-05 경계: 전부 0이어도 나누기 오류 없음
        public void Heatmap_AllZero()
        {
            var n = HeatmapMath.Normalize(new List<CellStat> { new CellStat { x = 0, y = 0 } }, HeatmapMetric.Wait);
            Assert.AreEqual(0f, n[Vector2Int.zero]);
        }

        [Test] // TC-VR-06
        public void RackPlacement_OnlyOnAisle()
        {
            var map = LoadW2();
            Vector2Int rack = default, aisle = default, wall = new Vector2Int(0, 0), dock = default;
            for (int x = 0; x < map.width; x++)
                for (int y = 0; y < map.height; y++)
                {
                    var t = map.GetCell(x, y);
                    if (t == CellType.Rack) rack = new Vector2Int(x, y);
                    if (t == CellType.Aisle) aisle = new Vector2Int(x, y);
                    if (t == CellType.DockIn) dock = new Vector2Int(x, y);
                }
            Assert.IsTrue(RackPlacement.CanPlace(map, aisle.x, aisle.y));
            Assert.IsFalse(RackPlacement.CanPlace(map, wall.x, wall.y));
            Assert.IsFalse(RackPlacement.CanPlace(map, dock.x, dock.y));
            Assert.IsFalse(RackPlacement.CanPlace(map, rack.x, rack.y));
            Assert.IsFalse(RackPlacement.CanPlace(map, -1, 0));
            // 칸 경계 근처(0.4칸 벗어남) → 가장 가까운 칸 중심으로 스냅
            Assert.AreEqual(aisle, GridCoord.LocalToCell(GridCoord.CellToLocal(aisle.x + 0.4f, aisle.y - 0.4f)));
            Assert.IsTrue(map.MoveRackCell(rack.x, rack.y, aisle.x, aisle.y));
            Assert.AreEqual(CellType.Rack, map.GetCell(aisle.x, aisle.y));
            Assert.AreEqual(CellType.Aisle, map.GetCell(rack.x, rack.y));
        }

        [Test] // IR-02 메시지 파싱: 최상위·data 아래 모두
        public void ServerMessage_ReadsFlatAndNested()
        {
            var a = ServerMessage.Parse("{\"type\":\"question\",\"question_id\":\"Q1\",\"error_cells\":[[4,2],[5,2]]}");
            Assert.AreEqual("question", a.Type);
            Assert.AreEqual("Q1", a.GetString("question_id"));
            Assert.AreEqual(2, a.GetCells("error_cells").Count);
            var b = ServerMessage.Parse("{\"type\":\"sim_ready\",\"data\":{\"sim_id\":\"S1\",\"total_steps\":120}}");
            Assert.AreEqual("S1", b.GetString("sim_id"));
            Assert.AreEqual(120, b.GetInt("total_steps"));
            Assert.IsTrue(ServerMessage.KnownTypes.Contains(b.Type));
        }

        [Test]
        public void Frames_ParseArrayOrWrapped()
        {
            Assert.AreEqual(1, SimParsers.ParseFrames("[{\"t\":0,\"robots\":[]}]").Count);
            Assert.AreEqual(1, SimParsers.ParseFrames("{\"frames\":[{\"t\":0,\"robots\":[]}]}").Count);
            Assert.AreEqual(1, SimParsers.ParseStats("{\"cells\":[{\"x\":1,\"y\":2,\"pass\":3,\"wait\":4}]}").Count);
        }

        [Test]
        public void Wav_HeaderAndLength()
        {
            var wav = WavEncoder.Encode(new float[16000], 16000);
            Assert.AreEqual(44 + 32000, wav.Length);
            Assert.AreEqual((byte)'R', wav[0]);
            Assert.AreEqual((byte)'W', wav[8]);
            Assert.AreEqual(0f, WavEncoder.Rms(new float[10]));
        }
    }
}

using ClassicUO.Assets;
using ClassicUO.IO;
using ClassicUO.Utility.Collections;
using Microsoft.Xna.Framework;
using static CentrED.Application;

namespace CentrED;

/// <summary>
/// Tracks animated statics and advances their frame offsets using the current game clock.
/// </summary>
// This class is a near-direct port of ClassicUO's animated static handling.
// In CentrED the caller supplies GameTime, so animation advancement is driven
// by the host game's clock instead of an internal timer.
sealed class AnimatedStaticsManager
    {
        private readonly FastList<StaticAnimationInfo> _staticInfos = new ();
        private uint _processTime;


        /// <summary>
        /// Scans tile data for animated statics and builds the internal animation state list.
        /// </summary>
        public unsafe void Initialize()
        {
            UOFile file = CEDGame.MapManager.UoFileManager.AnimData.AnimDataFile;

            if (file == null)
            {
                return;
            }

            // AnimData stores fixed-size records. Compute the highest valid
            // starting address once so we can skip indices whose record would
            // read past the end of the file.
            uint lastaddr = (uint)(file.Length - sizeof(AnimDataFrame));

            for (int i = 0; i < CEDGame.MapManager.UoFileManager.TileData.StaticData.Length; i++)
            {
                if (CEDGame.MapManager.UoFileManager.TileData.StaticData[i].IsAnimated)
                {
                    // The animdata.mul layout groups eight entries under a
                    // 4-byte header, so each record address includes both the
                    // per-entry stride and the per-group header offset.
                    uint addr = (uint)(i * 68 + 4 * (i / 8 + 1));

                    if (addr <= lastaddr)
                    {
                        _staticInfos.Add
                        (
                            new StaticAnimationInfo
                            {
                                Index = (ushort)i,
                                // IsField = StaticFilters.IsField((ushort)i)
                            }
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Advances any animated statics whose next frame is due for the provided game time.
        /// </summary>
        /// <param name="gameTime">The current game time used to determine animation deadlines.</param>
        public unsafe void Process(GameTime gameTime)
        {
            var ticks = (uint)gameTime.TotalGameTime.TotalMilliseconds;

            // _processTime stores the earliest animation deadline we found on
            // the previous pass. If we have not reached it yet, nothing needs
            // to be advanced this frame.
            if (_staticInfos == null || _staticInfos.Length == 0 || _processTime >= ticks)
            {
                return;
            }

            var file = CEDGame.MapManager.UoFileManager.AnimData.AnimDataFile;

            if (file == null)
            {
                return;
            }
            
            // Match the cadence used by the original client so animated statics
            // advance at the expected speed.
            uint delay = 50 * 2;
            uint next_time = ticks + 250;
            // bool no_animated_field = ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.FieldsType != 0;
            UOFileIndex[] static_data = CEDGame.MapManager.UoFileManager.Arts.File.Entries;

            for (int i = 0; i < _staticInfos.Length; i++)
            {
                ref StaticAnimationInfo o = ref _staticInfos.Buffer[i];

                // if (no_animated_field && o.IsField)
                // {
                //     o.AnimIndex = 0;
                //
                //     continue;
                // }

                if (o.Time < ticks)
                {
                    // Recompute the record address from the tile index so we
                    // can fetch the frame metadata for this animated static.
                    uint addr = (uint)(o.Index * 68 + 4 * (o.Index / 8 + 1));
                    file.Seek(addr, SeekOrigin.Begin);
                    var info = file.Read<AnimDataFrame>();

                    byte offset = o.AnimIndex;

                    if (info.FrameInterval > 0)
                    {
                        // FrameInterval is expressed in client ticks, so scale
                        // it by the fixed delay used above.
                        o.Time = ticks + info.FrameInterval * delay + 1;
                    }
                    else
                    {
                        // Some records omit an interval; fall back to the base
                        // delay so the animation still progresses.
                        o.Time = ticks + delay;
                    }

                    if (offset < info.FrameCount && o.Index + 0x4000 < static_data.Length)
                    {
                        // Animated static art lives in the static-art segment,
                        // which is addressed by adding 0x4000 to the tile id.
                        static_data[o.Index + 0x4000].AnimOffset = info.FrameData[offset++];
                    }

                    if (offset >= info.FrameCount)
                    {
                        // Loop back to the first frame once we reach the end of
                        // the frame list.
                        offset = 0;
                    }

                    o.AnimIndex = offset;
                }

                if (o.Time < next_time)
                {
                    // Track the soonest deadline so future Process calls can
                    // cheaply skip work until at least one animation is due.
                    next_time = o.Time;
                }
            }

            _processTime = next_time;
        }


        private struct StaticAnimationInfo
        {
            // Absolute time in milliseconds when this static should advance next.
            public uint Time;
            // TileData static index used to look up animdata and art entries.
            public ushort Index;
            // Current frame position within the animdata frame list.
            public byte AnimIndex;
            // public bool IsField;
        }
    }
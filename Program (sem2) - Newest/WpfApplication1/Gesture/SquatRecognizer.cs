using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using WpfApplication1.Helpers;

namespace WpfApplication1.Gesture
{
    public class SquatRecognizer
    {
        // Phases: 0 = Standing, 1 = Descending, 2 = Bottom, 3 = Ascending
        private int phase = 0;
        private float previousHipY = 0f;
        private bool standingHipInitialized = false;

        private string _phaseName = "standing";
        private double _currentHipDropCm = 0.0;

        public string PhaseName
        {
            get { return _phaseName; }
            private set { _phaseName = value ?? "standing"; }
        }

        public double CurrentHipDropCm
        {
            get { return _currentHipDropCm; }
            private set { _currentHipDropCm = value; }
        }

        // Depth/angle thresholds
        private float squatThreshold = 0.25f;
        private float standThreshold = 0.05f;
        private float minKneeAngle = 80f;

        // Trunk inclination thresholds (in degrees)
        private const double MinTrunkAngle = 10.0;
        private const double MaxTrunkAngle = 40.0;

        // Tracking quality thresholds
        private const float MinDistanceMeters = 0.50f; // too close -> pause

        // Depth category thresholds (centimeters)
        private const double DepthDeepCm = 40.0;    // at/above this = deep; otherwise normal

        // Pause state
        private bool _paused = false;
        private string _pauseReason = "";
        private bool _hasSeenTrackedBody = false;

        // LLM client and call control
        private readonly OllamaClient _ollama = new OllamaClient();
        private volatile bool _inflight = false;
        private int _callCounter = 0;

        // Two UI channels
        private readonly Action<string> _setHeadline; // frequent, generic HUD text
        private readonly Action<string> _postChat;    // full LLM block, once per rep

        public int SquatCount { get; private set; }

        // Stabilization for headline cadence only
        private int _stablePhase = 0;
        private int _candidatePhase = -1;
        private int _candidateFrames = 0;
        private DateTime _candidateStartTime = DateTime.MinValue;
        private const int StableFramesIn = 4;
        private const int StableDwellMs = 200;
        private DateTime _lastAckTime = DateTime.MinValue;
        private const int CooldownMs = 350;

        private string _lastHeadline = "";
        private float _phaseStartHipY = 0f;

        // Hysteresis state for stance flags (per current phase)
        private bool _prevLegsTooWide = false;
        private bool _prevLegsTooNarrow = false;

        // Session accumulation for one rep
        private class SquatSession
        {
            public int StartCountIndex;

            // Per-phase flags (unique, non-conflicting)
            public HashSet<string> StandingFlags = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> DescendingFlags = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> BottomFlags = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> AscendingFlags = new HashSet<string>(StringComparer.Ordinal);

            // Metrics mins/maxes across the rep
            public double LeftKneeAngleMin; public bool HasLeftKneeAngleMin;
            public double RightKneeAngleMin; public bool HasRightKneeAngleMin;
            public double TrunkAngleMin; public bool HasTrunkAngleMin;
            public double TrunkAngleMax; public bool HasTrunkAngleMax;
            public double TibiaAngleAvgMin; public bool HasTibiaAngleAvgMin;
            public double TibiaAngleAvgMax; public bool HasTibiaAngleAvgMax;

            public double MaxHipDropCm;

            // Squat type captured at rep end
            public string SquatType;
        }

        private class IssueSummary
        {
            public string Flag;
            public List<string> Phases;
            public int Count;
            public int Priority;
        }

        private class IssueNarrative
        {
            public string Paragraph;
            public List<IssueSummary> Summaries;
        }

        private SquatSession _currentSession = null;

        // Canonical, deterministic cues (exact text)
        private static readonly Dictionary<string, string> CueMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Stance / legs / feet
            { "legs_ok", "Legs and feet set well around shoulder width—keep that stance." },
            { "legs_too_narrow", "Legs too narrow—widen to shoulder width, keep knees tracking over toes." },
            { "legs_too_wide", "Legs too wide—bring heels in toward shoulder width, keep knees tracking over toes." },

            // Arms
            { "arms_ok", "Arms set well—keep them forward." },
            { "left_arm_extended", "Left arm—keep reaching forward to help balance." },
            { "right_arm_extended", "Right arm—keep reaching forward to help balance." },
            { "left_arm_not_extended", "Left arm—straighten and reach forward slightly." },
            { "right_arm_not_extended", "Right arm—straighten and reach forward slightly." },
            { "left_arm_too_high", "Left arm—lower to at/below shoulder height and reach forward." },
            { "right_arm_too_high", "Right arm—lower to at/below shoulder height and reach forward." },
            { "arms_not_extended", "Arms not extended—straighten and reach forward slightly." },
            { "arms_too_high", "Arms too high—lower to at/below shoulder height and reach forward." },

            // Trunk / chest
            { "trunk_ok", "Trunk angle looked good—ribs stacked, chest gently up." },
            { "trunk_too_upright", "Trunk too upright—add a small hip hinge from the hips." },
            { "trunk_too_forward", "Chest leaned forward—lift the chest and brace so ribs stay stacked." },

            // Bias at the bottom
            { "hip_dominant_bias", "Hips dominated—lift chest; let knees come slightly forward to match shins." },
            { "knee_dominant_bias", "Knees dominated—sit back slightly and brace; match torso to shins." },
            { "balanced_bias", "Torso and shins balanced—keep that alignment." },

            // Depth / symmetry
            { "left_knee_deep_enough", "Right knee—match the left side's depth." },
            { "right_knee_deep_enough", "Left knee—match the right side's depth." }
        };

        private static readonly string[] PhaseOrder = new string[] { "standing", "descending", "bottom", "ascending" };
        private static readonly string[] PhaseDisplayNames = new string[] { "Standing", "Descending", "Bottom", "Ascending" };
        private static readonly Dictionary<string, int> PhaseIndexMap = BuildPhaseIndexMap();

        private static readonly HashSet<string> IssueFlags = new HashSet<string>(StringComparer.Ordinal)
        {
            "legs_too_narrow",
            "legs_too_wide",
            "left_arm_not_extended",
            "left_arm_too_high",
            "right_arm_not_extended",
            "right_arm_too_high",
            "arms_not_extended",
            "arms_too_high",
            "trunk_too_upright",
            "trunk_too_forward"
        };

        private static readonly HashSet<string> BottomBiasFlags = new HashSet<string>(StringComparer.Ordinal)
        {
            "hip_dominant_bias",
            "knee_dominant_bias",
            "balanced_bias"
        };

        private static readonly Dictionary<string, string> IssueDictionary = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "legs_too_narrow", "Legs too narrow—widen to shoulder width, keep knees tracking over toes." },
            { "legs_too_wide", "Legs too wide—bring heels in toward shoulder width, keep knees tracking over toes." },
            { "left_arm_not_extended", "Left arm—straighten and reach forward slightly." },
            { "left_arm_too_high", "Left arm—lower to at/below shoulder height and reach forward." },
            { "right_arm_not_extended", "Right arm—straighten and reach forward slightly." },
            { "right_arm_too_high", "Right arm—lower to at/below shoulder height and reach forward." },
            { "arms_not_extended", "Arms not extended—straighten and reach forward slightly." },
            { "arms_too_high", "Arms too high—lower to at/below shoulder height and reach forward." },
            { "trunk_too_upright", "Trunk too upright—add a small hip hinge from the hips." },
            { "trunk_too_forward", "Chest leaned forward—lift the chest and brace so ribs stay stacked." },
            { "hip_dominant_bias", "Hips dominated—lift chest; let knees come slightly forward to match shins." },
            { "knee_dominant_bias", "Knees dominated—sit back slightly and brace; match torso to shins." }
        };

        private static readonly Dictionary<string, string[]> FamilyMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "legs", new string[] { "legs_ok", "legs_too_narrow", "legs_too_wide" } },
            { "trunk", new string[] { "trunk_ok", "trunk_too_upright", "trunk_too_forward" } },
            {
                "arms",
                new string[]
                {
                    "arms_ok",
                    "arms_not_extended",
                    "arms_too_high",
                    "left_arm_not_extended",
                    "left_arm_too_high",
                    "right_arm_not_extended",
                    "right_arm_too_high",
                    "left_arm_extended",
                    "right_arm_extended",
                    "left_arm_not_too_high",
                    "right_arm_not_too_high"
                }
            }
        };

        // Constructor with two callbacks (no UI calls made here)
        public SquatRecognizer(Action<string> setHeadline, Action<string> postChat)
        {
            _setHeadline = setHeadline ?? delegate { };
            _postChat = postChat ?? delegate { };
            SquatCount = 0;
            _stablePhase = 0;
            PhaseName = "standing";
            CurrentHipDropCm = 0.0;
        }

        // Back-compat (single callback -> headline only)
        public SquatRecognizer(Action<string> singleCallback)
            : this(singleCallback, delegate { }) { }

        public SquatRecognizer() : this(delegate { }, delegate { }) { }

        // UI calls this to resume after a pause
        public void ContinueAfterPause()
        {
            _paused = false;
            _pauseReason = "";
            phase = 0;
            _stablePhase = 0;
            _candidatePhase = -1;
            _candidateFrames = 0;
            _candidateStartTime = DateTime.MinValue;
            standingHipInitialized = false; // re-sample standing height
            _currentSession = null;
            _lastHeadline = "";
            _hasSeenTrackedBody = false; // re-arm gate
            _prevLegsTooWide = false;
            _prevLegsTooNarrow = false;
            PhaseName = "standing";
            CurrentHipDropCm = 0.0;
            SafeSetHeadline("Ready for squats!");
        }

        public bool IsPaused { get { return _paused; } }
        public string PauseReason { get { return _pauseReason; } }

        public void Update(
            Body body,
            CoordinateMapper coordinateMapper,
            ushort[] depthData,
            byte[] bodyIndexData,
            int depthWidth,
            int depthHeight,
            int bodyIndex)
        {
            if (_paused)
            {
                SafeSetHeadline("Paused: " + (_pauseReason ?? "tracking unreliable") + ". Tap Continue to resume.");
                PhaseName = "paused";
                CurrentHipDropCm = 0.0;
                return;
            }

            // Validate tracking quality
            string reason;
            bool reliable = IsTrackingReliable(body, out reason);

            if (!_hasSeenTrackedBody)
            {
                if (reliable) _hasSeenTrackedBody = true;
                if (!reliable)
                {
                    SafeSetHeadline("Step into the frame");
                    PhaseName = "no_body";
                    CurrentHipDropCm = 0.0;
                    return;
                }
            }
            else
            {
                if (!reliable)
                {
                    EnterPause(reason);
                    PhaseName = "paused";
                    CurrentHipDropCm = 0.0;
                    return;
                }
            }

            var joints = body.Joints;

            // Key joints
            var hipLeft = joints[JointType.HipLeft].Position;
            var hipRight = joints[JointType.HipRight].Position;
            float hipY = (hipLeft.Y + hipRight.Y) / 2f;

            var kneeLeft = joints[JointType.KneeLeft].Position;
            var kneeRight = joints[JointType.KneeRight].Position;

            var ankleLeft = joints[JointType.AnkleLeft].Position;
            var ankleRight = joints[JointType.AnkleRight].Position;

            var shoulderLeft = joints[JointType.ShoulderLeft].Position;
            var shoulderRight = joints[JointType.ShoulderRight].Position;
            float shoulderWidth = Math.Abs(shoulderLeft.X - shoulderRight.X);
            float ankleWidth = Math.Abs(ankleLeft.X - ankleRight.X);

            // Stance flags with widened band and hysteresis
            const float MinRatio = 0.90f;
            const float MaxRatio = 1.28f;
            const float HysteresisMeters = 0.02f;

            float minAnkleWidth = MinRatio * shoulderWidth;
            float maxAnkleWidth = MaxRatio * shoulderWidth;

            float minWithHyst = _prevLegsTooNarrow ? (minAnkleWidth + HysteresisMeters) : minAnkleWidth;
            float maxWithHyst = _prevLegsTooWide ? (maxAnkleWidth - HysteresisMeters) : maxAnkleWidth;

            bool legsTooNarrow = ankleWidth < minWithHyst;
            bool legsTooWide = ankleWidth > maxWithHyst;
            bool legsOk = !legsTooNarrow && !legsTooWide;

            _prevLegsTooNarrow = legsTooNarrow;
            _prevLegsTooWide = legsTooWide;

            // Arms measurements
            var elbowLeft = joints[JointType.ElbowLeft].Position;
            var elbowRight = joints[JointType.ElbowRight].Position;

            bool leftArmExtended = (elbowLeft.Z < shoulderLeft.Z - 0.05f);
            bool rightArmExtended = (elbowRight.Z < shoulderRight.Z - 0.05f);

            bool leftArmNotTooHigh = (elbowLeft.Y < shoulderLeft.Y + 0.05f);
            bool rightArmNotTooHigh = (elbowRight.Y < shoulderRight.Y + 0.05f);

            // Init standing height
            if (!standingHipInitialized)
            {
                previousHipY = hipY;
                standingHipInitialized = true;
            }
            float standingHipHeight = previousHipY;

            // Angles
            double leftKneeAngle = KinectHelpers.CalculateJointAngle(
                joints[JointType.HipLeft], joints[JointType.KneeLeft], joints[JointType.AnkleLeft],
                coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex);

            double rightKneeAngle = KinectHelpers.CalculateJointAngle(
                joints[JointType.HipRight], joints[JointType.KneeRight], joints[JointType.AnkleRight],
                coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex);

            // Trunk inclination
            var spineBase = joints[JointType.SpineBase].Position;
            var spineShoulder = joints[JointType.SpineShoulder].Position;
            float trunkDX = spineShoulder.X - spineBase.X;
            float trunkDY = spineShoulder.Y - spineBase.Y;
            float trunkDZ = spineShoulder.Z - spineBase.Z;
            double trunkVectorLength = Math.Sqrt(trunkDX * trunkDX + trunkDY * trunkDY + trunkDZ * trunkDZ);
            double trunkCosAngle = trunkDY / trunkVectorLength;
            double trunkAngle = Math.Acos(trunkCosAngle) * (180.0 / Math.PI);

            bool trunkOk = (trunkAngle >= MinTrunkAngle && trunkAngle <= MaxTrunkAngle);
            bool trunkTooUpright = trunkAngle < MinTrunkAngle;
            bool trunkTooForward = trunkAngle > MaxTrunkAngle;

            // Tibia angles (avg)
            float leftTibiaDX = kneeLeft.X - ankleLeft.X;
            float leftTibiaDY = kneeLeft.Y - ankleLeft.Y;
            float leftTibiaDZ = kneeLeft.Z - ankleLeft.Z;
            double leftTibiaLength = Math.Sqrt(leftTibiaDX * leftTibiaDX + leftTibiaDY * leftTibiaDY + leftTibiaDZ * leftTibiaDZ);
            double leftTibiaCosAngle = leftTibiaDY / leftTibiaLength;
            double leftTibiaAngle = Math.Acos(leftTibiaCosAngle) * (180.0 / Math.PI);

            float rightTibiaDX = kneeRight.X - ankleRight.X;
            float rightTibiaDY = kneeRight.Y - ankleRight.Y;
            float rightTibiaDZ = kneeRight.Z - ankleRight.Z;
            double rightTibiaLength = Math.Sqrt(rightTibiaDX * rightTibiaDX + rightTibiaDY * rightTibiaDY + rightTibiaDZ * rightTibiaDZ);
            double rightTibiaCosAngle = rightTibiaDY / rightTibiaLength;
            double rightTibiaAngle = Math.Acos(rightTibiaCosAngle) * (180.0 / Math.PI);

            double tibiaAngle = (leftTibiaAngle + rightTibiaAngle) / 2.0;

            // Bias (info captured at bottom)
            float trunkTibiaDiff = (float)(trunkAngle - tibiaAngle);
            bool hipDominantBias = trunkTibiaDiff > 10f;
            bool kneeDominantBias = trunkTibiaDiff < -10f;
            bool balancedBias = Math.Abs(trunkTibiaDiff) <= 10f;

            // Depth / phase progression measures (pure kinematics)
            float hipDrop = standingHipHeight - hipY;
            bool kneesAtDepth = (leftKneeAngle <= minKneeAngle || rightKneeAngle <= minKneeAngle);

            // Begin/continue session
            EnsureSession();

            // Accumulate per-phase data
            string phaseName = phase == 0 ? "standing" :
                               phase == 1 ? "descending" :
                               phase == 2 ? "bottom" : "ascending";

            List<string> flags = new List<string>();

            // Legs stance flags
            if (legsOk) flags.Add("legs_ok");
            else
            {
                if (legsTooNarrow) flags.Add("legs_too_narrow");
                else if (legsTooWide) flags.Add("legs_too_wide");
            }

            // Arm flags (normalized)
            string leftArmIssue = null;
            string rightArmIssue = null;

            if (!leftArmExtended) leftArmIssue = "left_arm_not_extended";
            else if (!leftArmNotTooHigh) leftArmIssue = "left_arm_too_high";

            if (!rightArmExtended) rightArmIssue = "right_arm_not_extended";
            else if (!rightArmNotTooHigh) rightArmIssue = "right_arm_too_high";

            if (leftArmIssue == null && rightArmIssue == null)
            {
                flags.Add("arms_ok");
            }
            else if (leftArmIssue != null && leftArmIssue == rightArmIssue)
            {
                if (leftArmIssue.IndexOf("not_extended", StringComparison.Ordinal) >= 0)
                    flags.Add("arms_not_extended");
                else if (leftArmIssue.IndexOf("too_high", StringComparison.Ordinal) >= 0)
                    flags.Add("arms_too_high");
                else
                {
                    flags.Add(leftArmIssue);
                    flags.Add(rightArmIssue);
                }
            }
            else
            {
                if (leftArmIssue != null) flags.Add(leftArmIssue);
                if (rightArmIssue != null) flags.Add(rightArmIssue);
            }

            // Trunk flags
            if (trunkOk) flags.Add("trunk_ok");
            else
            {
                if (trunkTooUpright) flags.Add("trunk_too_upright");
                else if (trunkTooForward) flags.Add("trunk_too_forward");
            }

            // Bias flags at bottom only
            if (phase == 2)
            {
                if (hipDominantBias) flags.Add("hip_dominant_bias");
                else if (kneeDominantBias) flags.Add("knee_dominant_bias");
                else if (balancedBias) flags.Add("balanced_bias");
            }

            // Update session metrics and max hip drop
            double hipDepthCm = Math.Max(0.0, hipDrop * 100.0);
            AccumulatePhaseData(_currentSession, phaseName, flags,
                                leftKneeAngle, rightKneeAngle, trunkAngle, tibiaAngle,
                                hipDepthCm);

            // PHASE STATE MACHINE (with abort handling)
            string phaseFeedbackForHUD = "";

            const float standAbortThreshold = 0.07f;
            const float ascendReverseTolerance = 0.01f;

            switch (phase)
            {
                case 0:
                    if (hipDrop > squatThreshold)
                    {
                        phase = 1;
                        _phaseStartHipY = hipY;
                        phaseFeedbackForHUD = "Squatting down!";
                    }
                    else
                    {
                        if (legsTooNarrow) phaseFeedbackForHUD = "Feet too close!";
                        else if (legsTooWide) phaseFeedbackForHUD = "Feet too wide!";
                        else if (trunkTooUpright) phaseFeedbackForHUD = "Hinge slightly from hips.";
                        else if (trunkTooForward) phaseFeedbackForHUD = "Chest too far forward.";
                        else phaseFeedbackForHUD = "Stand tall!";
                    }
                    break;

                case 1:
                    if (kneesAtDepth)
                    {
                        phase = 2;
                        _phaseStartHipY = hipY;
                        phaseFeedbackForHUD = "At bottom, now stand up!";
                    }
                    else
                    {
                        float hipDropNow = standingHipHeight - hipY;
                        if (hipDropNow < standAbortThreshold)
                        {
                            ResetToStandingAbort("Aborted descent (returned to standing without reaching depth)");
                            phaseFeedbackForHUD = "Reset to standing";
                            break;
                        }
                        phaseFeedbackForHUD = "Go lower!";
                    }
                    break;

                case 2:
                    if (hipDrop < 0.20f)
                    {
                        phase = 3;
                        _phaseStartHipY = hipY;
                        phaseFeedbackForHUD = "Standing up!";
                    }
                    break;

                case 3:
                    {
                        float hipDropNow = standingHipHeight - hipY;
                        float hipDropAtPhaseStart = standingHipHeight - _phaseStartHipY;
                        bool reversedIntoDescent = (hipDropNow > hipDropAtPhaseStart + ascendReverseTolerance);
                        if (reversedIntoDescent)
                        {
                            ResetToStandingAbort("Aborted ascent (descended again before standing)");
                            phaseFeedbackForHUD = "Reset to standing";
                            break;
                        }
                    }

                    if (hipDrop < standThreshold)
                    {
                        // Rep completed
                        SquatCount++;

                        if (_currentSession != null)
                        {
                            _currentSession.SquatType = ClassifySquat(_currentSession.MaxHipDropCm);
                        }

                        phaseFeedbackForHUD = "Squat counted!";
                        phase = 0;
                        previousHipY = hipY;

                        FinalizeRepAndCallLLM();
                    }
                    break;

                default:
                    phase = 0;
                    break;
            }

            // Stabilize HUD headline cadence
            if ((DateTime.UtcNow - _lastAckTime).TotalMilliseconds < CooldownMs)
            {
                SafeSetHeadline(phaseFeedbackForHUD);
                PhaseName = phase == 0 ? "standing" :
                            phase == 1 ? "descending" :
                            phase == 2 ? "bottom" : "ascending";
                CurrentHipDropCm = Math.Max(0.0, (standingHipHeight - hipY) * 100.0);
                return;
            }

            if (_candidatePhase == phase) _candidateFrames++;
            else
            {
                _candidatePhase = phase;
                _candidateFrames = 1;
                _candidateStartTime = DateTime.UtcNow;
            }

            bool qualifiesFrames = _candidateFrames >= StableFramesIn;
            bool qualifiesTime = (DateTime.UtcNow - _candidateStartTime).TotalMilliseconds >= StableDwellMs;

            if (qualifiesFrames && qualifiesTime && _stablePhase != _candidatePhase)
            {
                _stablePhase = _candidatePhase;
                _lastAckTime = DateTime.UtcNow;
            }

            SafeSetHeadline(phaseFeedbackForHUD);

            // Update debug HUD snapshot (end of Update)
            PhaseName = phase == 0 ? "standing" :
                        phase == 1 ? "descending" :
                        phase == 2 ? "bottom" : "ascending";
            CurrentHipDropCm = Math.Max(0.0, (standingHipHeight - hipY) * 100.0);
        }

        private string ClassifySquat(double maxHipDropCm)
        {
            return (maxHipDropCm >= DepthDeepCm) ? "deep squat" : "normal squat";
        }

        private bool IsTrackingReliable(Body body, out string reason)
        {
            reason = null;

            if (body == null || !body.IsTracked)
            {
                reason = "No body detected or out of frame";
                return false;
            }

            Joint spineBaseJoint = body.Joints[JointType.SpineBase];
            if (spineBaseJoint.TrackingState == TrackingState.Tracked)
            {
                if (spineBaseJoint.Position.Z < MinDistanceMeters)
                {
                    reason = "Too close to the sensor (step back > 50 cm)";
                    return false;
                }
            }

            JointType[] critical = new JointType[]
            {
                JointType.SpineBase, JointType.SpineMid, JointType.SpineShoulder,
                JointType.ShoulderLeft, JointType.ElbowLeft, JointType.WristLeft,
                JointType.ShoulderRight, JointType.ElbowRight, JointType.WristRight,
                JointType.HipLeft, JointType.KneeLeft, JointType.AnkleLeft,
                JointType.HipRight, JointType.KneeRight, JointType.AnkleRight
            };

            for (int i = 0; i < critical.Length; i++)
            {
                Joint j = body.Joints[critical[i]];
                if (j.TrackingState == TrackingState.NotTracked)
                {
                    reason = "Joints not tracked (step into the frame)";
                    return false;
                }
            }

            return true;
        }

        private void EnterPause(string reason)
        {
            _paused = true;
            _pauseReason = reason ?? "tracking unreliable";
            phase = 0;
            _stablePhase = 0;
            _candidatePhase = -1;
            _candidateFrames = 0;
            _candidateStartTime = DateTime.MinValue;
            standingHipInitialized = false;
            _currentSession = null;
            _lastHeadline = "";
            _prevLegsTooWide = false;
            _prevLegsTooNarrow = false;
            SafeSetHeadline("Paused: " + _pauseReason + ". Tap Continue to resume.");
        }

        private void EnsureSession()
        {
            if (_currentSession == null)
            {
                _currentSession = new SquatSession();
                _currentSession.StartCountIndex = SquatCount + 1;
                _currentSession.MaxHipDropCm = 0.0;
                _currentSession.SquatType = null;
            }
        }

        private static void AccumulatePhaseData(
            SquatSession s,
            string phaseName,
            List<string> flagsThisFrame,
            double leftKneeAngle,
            double rightKneeAngle,
            double trunkAngle,
            double tibiaAngleAvg,
            double hipDepthCm)
        {
            if (s == null) return;

            HashSet<string> target =
                phaseName == "standing" ? s.StandingFlags :
                phaseName == "descending" ? s.DescendingFlags :
                phaseName == "bottom" ? s.BottomFlags :
                s.AscendingFlags;

            if (flagsThisFrame != null && flagsThisFrame.Count > 0)
            {
                for (int i = 0; i < flagsThisFrame.Count; i++)
                {
                    AddNormalizedFlag(target, flagsThisFrame[i]);
                }

                NormalizeArmFlags(target);
            }

            if (hipDepthCm > s.MaxHipDropCm) s.MaxHipDropCm = hipDepthCm;

            if (!s.HasLeftKneeAngleMin || leftKneeAngle < s.LeftKneeAngleMin) { s.LeftKneeAngleMin = leftKneeAngle; s.HasLeftKneeAngleMin = true; }
            if (!s.HasRightKneeAngleMin || rightKneeAngle < s.RightKneeAngleMin) { s.RightKneeAngleMin = rightKneeAngle; s.HasRightKneeAngleMin = true; }
            if (!s.HasTrunkAngleMin || trunkAngle < s.TrunkAngleMin) { s.TrunkAngleMin = trunkAngle; s.HasTrunkAngleMin = true; }
            if (!s.HasTrunkAngleMax || trunkAngle > s.TrunkAngleMax) { s.TrunkAngleMax = trunkAngle; s.HasTrunkAngleMax = true; }
            if (!s.HasTibiaAngleAvgMin || tibiaAngleAvg < s.TibiaAngleAvgMin) { s.TibiaAngleAvgMin = tibiaAngleAvg; s.HasTibiaAngleAvgMin = true; }
            if (!s.HasTibiaAngleAvgMax || tibiaAngleAvg > s.TibiaAngleAvgMax) { s.TibiaAngleAvgMax = tibiaAngleAvg; s.HasTibiaAngleAvgMax = true; }
        }

        private void FinalizeRepAndCallLLM()
        {
            SquatSession session = _currentSession;
            _currentSession = null;

            if (session == null) return;
            if (_inflight) return;

            string squatType = session.SquatType;
            if (string.IsNullOrEmpty(squatType))
            {
                squatType = ClassifySquat(session.MaxHipDropCm);
            }

            string bottomBias = DetermineBottomBias(session.BottomFlags);
            IssueNarrative narrative = BuildIssueNarrative(session);
            string issueParagraph = narrative.Paragraph ?? "";

            // Build the formatted string for debug logging
            StringBuilder flagsForDebug = new StringBuilder();
            flagsForDebug.AppendLine("Standing: " + FormatFlagList(session.StandingFlags));
            flagsForDebug.AppendLine("Descending: " + FormatFlagList(session.DescendingFlags));
            flagsForDebug.AppendLine("Bottom: " + FormatFlagList(session.BottomFlags));
            flagsForDebug.AppendLine("Ascending: " + FormatFlagList(session.AscendingFlags).TrimEnd());
            string formattedFlags = flagsForDebug.ToString();

            string summaryJson = BuildSummaryJson(squatType, session.MaxHipDropCm, bottomBias);
            string prompt = BuildSummarizationPrompt(squatType, bottomBias, summaryJson, issueParagraph);

            _inflight = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string reply;
                try
                {
                    // Pass the formatted flags to the API call for logging
                    reply = _ollama.ApiCallDeterministicOverall(prompt, formattedFlags);

                    if (string.IsNullOrEmpty(reply) || OllamaClient.IsNullOrWhiteSpace35(reply))
                    {
                        reply = BuildFallbackSummary(squatType, bottomBias, narrative);
                    }
                    else
                    {
                        reply = OllamaClient.NormalizeOutput(reply);
                    }
                }
                catch (Exception ex)
                {
                    reply = BuildFallbackSummary(squatType, bottomBias, narrative) + "\nLLM error: " + ex.Message;
                }
                finally
                {
                    _inflight = false;
                }

                string actionSentence = BuildActionSentence(narrative);
                reply = AppendActionSentence(reply, actionSentence);

                SafePostChat(reply);
            });
        }

        private static string DetermineBottomBias(HashSet<string> bottomFlags)
        {
            if (bottomFlags != null)
            {
                if (bottomFlags.Contains("hip_dominant_bias")) return "hips dominated bias";
                if (bottomFlags.Contains("knee_dominant_bias")) return "knees dominated bias";
                if (bottomFlags.Contains("balanced_bias")) return "balanced bias";
            }
            return "neutral bias";
        }

        private static string BuildSummaryJson(string squatType, double maxDepthCm, string bottomBias)
        {
            double roundedDepth = Math.Round(maxDepthCm, 1, MidpointRounding.AwayFromZero);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"squatType\":\"");
            sb.Append(JsonEscape(squatType));
            sb.Append("\",\"maxDepthCm\":");
            sb.Append(roundedDepth.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append(",\"bottomBias\":\"");
            sb.Append(JsonEscape(bottomBias));
            sb.Append("\"}");
            return sb.ToString();
        }

        private static string BuildSummarizationPrompt(string squatType, string bottomBias, string summaryJson, string issueParagraph)
        {
            string expectedFirstSentence = squatType + " with " + bottomBias + ".";
            StringBuilder sb = new StringBuilder(1024);

            // Add language instruction first
            string language = AppSettings.SelectedLanguage;
            if (language != "English")
            {
                sb.AppendLine("IMPORTANT: You must reply in " + language + ".");
            }

            // Add personality reminder
            string selectedModel = AppSettings.SelectedCoachModel;
            if (selectedModel == "friendly3bmcoach")
            {
                sb.AppendLine("REMEMBER: Your persona is a friendly, funny, and sarcastic coach. Your response must reflect this personality.");
            }
            else if (selectedModel == "strict3bmcoach")
            {
                sb.AppendLine("REMEMBER: Your persona is a strict Drill Sergeant addressing a recruit. Your response must reflect this personality.");
            }

            sb.AppendLine("You are a coaching assistant summarizing a squat analysis.");
            sb.AppendLine("Follow these rules exactly:");
            sb.AppendLine("1. Sentence 1 must be exactly: " + expectedFirstSentence);
            sb.AppendLine("2. Write 1 to 2 additional sentences that concisely summarise the main issues described in the paragraph, using only the provided facts.");
            sb.AppendLine("3. If the paragraph states that no issues were present, emphasise consistent technique instead of inventing problems.");
            sb.AppendLine("4. Keep the entire summary to at most 3 sentences and end with <END>.");
            sb.AppendLine("5. Do not invent new details, avoid phase-by-phase lists, and do not include explicit action or prescription sentences.");
            sb.AppendLine();
            sb.AppendLine("JSON:");
            sb.AppendLine(summaryJson);
            sb.AppendLine();
            sb.AppendLine("Issue paragraph:");
            sb.AppendLine(issueParagraph);
            return sb.ToString();
        }

        private static IssueNarrative BuildIssueNarrative(SquatSession session)
        {
            IssueNarrative narrative = new IssueNarrative();
            Dictionary<string, List<string>> issueTimeline = CompileIssueTimeline(session);
            List<IssueSummary> summaries = BuildIssueSummaries(issueTimeline);
            narrative.Summaries = summaries;

            if (summaries.Count == 0)
            {
                narrative.Paragraph = "No non-bias issues were detected across the phases; posture and control remained consistent.";
                return narrative;
            }

            StringBuilder paragraph = new StringBuilder();
            for (int i = 0; i < summaries.Count; i++)
            {
                if (i > 0) paragraph.Append(' ');
                paragraph.Append(RenderIssueSentence(summaries[i]));
            }
            narrative.Paragraph = paragraph.ToString();
            return narrative;
        }

        private static Dictionary<string, List<string>> CompileIssueTimeline(SquatSession session)
        {
            Dictionary<string, List<string>> timeline = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            AddPhaseFlags(timeline, session.StandingFlags, "standing");
            AddPhaseFlags(timeline, session.DescendingFlags, "descending");
            AddPhaseFlags(timeline, session.BottomFlags, "bottom");
            AddPhaseFlags(timeline, session.AscendingFlags, "ascending");

            foreach (KeyValuePair<string, List<string>> kvp in timeline)
            {
                List<string> phases = kvp.Value;
                phases.Sort(new PhaseComparer());
            }

            return timeline;
        }

        private static void AddPhaseFlags(Dictionary<string, List<string>> timeline, HashSet<string> flags, string phaseName)
        {
            if (flags == null || flags.Count == 0) return;

            foreach (string flag in flags)
            {
                if (!IssueFlags.Contains(flag)) continue;

                List<string> phases;
                if (!timeline.TryGetValue(flag, out phases))
                {
                    phases = new List<string>();
                    timeline[flag] = phases;
                }
                if (!phases.Contains(phaseName))
                {
                    phases.Add(phaseName);
                }
            }
        }

        private static List<IssueSummary> BuildIssueSummaries(Dictionary<string, List<string>> timeline)
        {
            List<IssueSummary> list = new List<IssueSummary>();
            foreach (KeyValuePair<string, List<string>> kvp in timeline)
            {
                IssueSummary summary = new IssueSummary();
                summary.Flag = kvp.Key;
                summary.Phases = kvp.Value;
                summary.Count = kvp.Value.Count;
                summary.Priority = GetIssuePriority(kvp.Key);
                list.Add(summary);
            }

            list.Sort(delegate(IssueSummary a, IssueSummary b)
            {
                int countCompare = b.Count.CompareTo(a.Count);
                if (countCompare != 0) return countCompare;
                int priorityCompare = a.Priority.CompareTo(b.Priority);
                if (priorityCompare != 0) return priorityCompare;
                return string.Compare(a.Flag, b.Flag, StringComparison.Ordinal);
            });

            return list;
        }

        private static string RenderIssueSentence(IssueSummary summary)
        {
            string label = HumanizeFlagId(summary.Flag);
            List<string> phases = summary.Phases;
            if (phases == null || phases.Count == 0)
            {
                return label + " was noted, but the phase information was unavailable.";
            }

            int firstIndex = PhaseIndexMap[phases[0]];
            int lastIndex = PhaseIndexMap[phases[phases.Count - 1]];

            bool reappeared = false;
            for (int i = 1; i < phases.Count; i++)
            {
                int currentIndex = PhaseIndexMap[phases[i]];
                int previousIndex = PhaseIndexMap[phases[i - 1]];
                if (currentIndex - previousIndex > 1)
                {
                    reappeared = true;
                    break;
                }
            }

            StringBuilder sentence = new StringBuilder();
            sentence.Append(label);
            sentence.Append(" first appeared in ");
            sentence.Append(PhaseDisplayNames[firstIndex]);

            if (reappeared)
            {
                sentence.Append(", was absent temporarily, and reappeared in ");
                sentence.Append(PhaseDisplayNames[lastIndex]);
                sentence.Append('.');
                return sentence.ToString();
            }

            if (lastIndex == PhaseOrder.Length - 1)
            {
                sentence.Append(" and persisted through ");
                sentence.Append(PhaseDisplayNames[lastIndex]);
                sentence.Append('.');
                return sentence.ToString();
            }

            string correctionPhase = PhaseDisplayNames[lastIndex + 1];
            if (firstIndex == lastIndex)
            {
                sentence.Append(" and was corrected by ");
                sentence.Append(correctionPhase);
                sentence.Append('.');
            }
            else
            {
                sentence.Append(" and persisted through ");
                sentence.Append(PhaseDisplayNames[lastIndex]);
                sentence.Append(" before being corrected by ");
                sentence.Append(correctionPhase);
                sentence.Append('.');
            }

            return sentence.ToString();
        }

        private static string BuildFallbackSummary(string squatType, string bottomBias, IssueNarrative narrative)
        {
            bool hasIssues = narrative != null && narrative.Summaries != null && narrative.Summaries.Count > 0;
            string firstSentence = squatType + " with " + bottomBias + ".";
            string secondSentence = hasIssues
                ? "Review the captured issue timeline manually because the language model response was unavailable."
                : "Technique appeared consistent across phases; manual review is still recommended.";
            return firstSentence + " " + secondSentence + " <END>";
        }

        private void ResetToStandingAbort(string reason)
        {
            _currentSession = null;
            phase = 0;
            standingHipInitialized = false;
            _lastHeadline = "";
            _prevLegsTooWide = false;
            _prevLegsTooNarrow = false;
            Console.WriteLine("[Abort] " + reason);
        }

        // Headline helper with local dedup
        private void SafeSetHeadline(string headline)
        {
            headline = headline ?? "";
            if (string.Equals(_lastHeadline, headline, StringComparison.Ordinal)) return;
            _lastHeadline = headline;
            _setHeadline(headline);
        }

        // Chat helper
        private void SafePostChat(string chatBlock)
        {
            if (WpfApplication1.OllamaClient.IsNullOrWhiteSpace35(chatBlock)) return;
            _postChat(chatBlock);
        }

        private static string FormatFlagList(HashSet<string> flags)
        {
            if (flags == null || flags.Count == 0) return "(none)";
            List<string> list = new List<string>(flags);
            list.Sort(StringComparer.Ordinal);
            return string.Join(", ", list.ToArray());
        }

        private static string HumanizeFlagId(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return "Issue";
            string[] parts = flag.Split(new char[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return flag;

            StringBuilder sb = new StringBuilder(flag.Length + parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].ToLowerInvariant();
                if (part.Length == 0) continue;

                if (sb.Length > 0)
                    sb.Append(' ');

                if (i == 0)
                {
                    sb.Append(char.ToUpper(part[0], CultureInfo.InvariantCulture));
                    if (part.Length > 1)
                        sb.Append(part.Substring(1));
                }
                else
                {
                    sb.Append(part);
                }
            }

            return sb.Length > 0 ? sb.ToString() : flag;
        }

        private static int GetIssuePriority(string flag)
        {
            if (flag.StartsWith("legs_", StringComparison.Ordinal)) return 1;
            if (flag.StartsWith("trunk_", StringComparison.Ordinal)) return 2;
            if (flag.StartsWith("arms_", StringComparison.Ordinal)) return 3;
            if (flag.StartsWith("left_arm", StringComparison.Ordinal) || flag.StartsWith("right_arm", StringComparison.Ordinal)) return 3;
            return 4;
        }

        private static void NormalizeArmFlags(HashSet<string> flags)
        {
            if (flags == null) return;

            if (flags.Contains("left_arm_not_extended") && flags.Contains("right_arm_not_extended"))
            {
                flags.Remove("left_arm_not_extended");
                flags.Remove("right_arm_not_extended");
                flags.Add("arms_not_extended");
            }

            if (flags.Contains("left_arm_too_high") && flags.Contains("right_arm_too_high"))
            {
                flags.Remove("left_arm_too_high");
                flags.Remove("right_arm_too_high");
                flags.Add("arms_too_high");
            }
        }

        private static void AddNormalizedFlag(HashSet<string> target, string flag)
        {
            if (target == null || string.IsNullOrEmpty(flag)) return;

            string family = GetFamilyForFlag(flag);
            if (family != null)
            {
                RemoveFamilyFlags(target, family);
            }

            target.Add(flag);
        }

        private static void RemoveFamilyFlags(HashSet<string> target, string family)
        {
            if (target == null || string.IsNullOrEmpty(family)) return;

            string[] members;
            if (!FamilyMap.TryGetValue(family, out members)) return;

            for (int i = 0; i < members.Length; i++)
            {
                target.Remove(members[i]);
            }
        }

        private static string GetFamilyForFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return null;

            if (flag.StartsWith("legs_", StringComparison.Ordinal)) return "legs";
            if (flag.StartsWith("trunk_", StringComparison.Ordinal)) return "trunk";
            if (flag.IndexOf("arm", StringComparison.Ordinal) >= 0 || flag.StartsWith("arms_", StringComparison.Ordinal))
                return "arms";
            return null;
        }

        // Build an explicit body-part action sentence based on the most frequent issue
        private static string BuildActionSentence(IssueNarrative narrative)
        {
            if (narrative == null || narrative.Summaries == null || narrative.Summaries.Count == 0)
                return null;

            string flag = narrative.Summaries[0].Flag;
            string action = GetBodyPartSpecificAction(flag);

            if (string.IsNullOrEmpty(action))
                action = "Address " + HumanizeFlagId(flag).ToLowerInvariant() + " on the next rep.";

            // Capitalize and ensure terminal punctuation
            action = CapitalizeFirst(action);
            if (action.Length == 0 || (action[action.Length - 1] != '.' && action[action.Length - 1] != '!' && action[action.Length - 1] != '?'))
                action += ".";

            return "Action: " + action;
        }

        private static string GetBodyPartSpecificAction(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return "";

            // Legs / stance
            if (flag == "legs_too_wide") return "Legs: bring heels in toward shoulder width; keep knees tracking over toes";
            if (flag == "legs_too_narrow") return "Legs: widen stance to shoulder width; keep knees tracking over toes";

            // Arms
            if (flag == "arms_not_extended") return "Arms: straighten both arms and reach forward slightly at shoulder height";
            if (flag == "left_arm_not_extended") return "Left arm: straighten and reach forward slightly at shoulder height";
            if (flag == "right_arm_not_extended") return "Right arm: straighten and reach forward slightly at shoulder height";
            if (flag == "arms_too_high") return "Arms: lower to at or just below shoulder height while reaching forward";
            if (flag == "left_arm_too_high") return "Left arm: lower to at or just below shoulder height while reaching forward";
            if (flag == "right_arm_too_high") return "Right arm: lower to at or just below shoulder height while reaching forward";

            // Trunk
            if (flag == "trunk_too_upright") return "Trunk: add a small hip hinge from the hips to bring the chest slightly forward";
            if (flag == "trunk_too_forward") return "Trunk: lift the chest and brace to keep ribs stacked over the pelvis";

            // Bias (fallback only)
            if (flag == "hip_dominant_bias") return "Trunk and knees: lift the chest and allow the knees to come slightly forward to match shin angle";
            if (flag == "knee_dominant_bias") return "Hips and trunk: sit back slightly and brace so the torso better matches shin angle";

            return "";
        }

        private static string AppendActionSentence(string summary, string actionSentence)
        {
            if (string.IsNullOrEmpty(summary) || string.IsNullOrEmpty(actionSentence))
            {
                return summary;
            }

            string trimmed = summary.TrimEnd();
            int idx = trimmed.LastIndexOf("<END>", StringComparison.OrdinalIgnoreCase);
            bool hadEnd = idx >= 0;
            if (hadEnd)
            {
                trimmed = trimmed.Substring(0, idx).TrimEnd();
            }

            if (trimmed.Length > 0 && !char.IsWhiteSpace(trimmed[trimmed.Length - 1]))
            {
                trimmed += " ";
            }
            trimmed += actionSentence;

            trimmed = trimmed.TrimEnd();
            trimmed += " <END>";
            return trimmed;
        }

        private static string ExtractCueAction(string cue)
        {
            if (string.IsNullOrEmpty(cue)) return "";
            int dash = cue.IndexOf('—');
            if (dash < 0) dash = cue.IndexOf('-');
            string action = dash >= 0 && dash + 1 < cue.Length ? cue.Substring(dash + 1).Trim() : cue.Trim();
            return TrimEndingPeriod(action);
        }

        private static string TrimEndingPeriod(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = text.TrimEnd();
            while (result.Length > 0)
            {
                char last = result[result.Length - 1];
                if (last == '.' || last == '!' || last == '?')
                {
                    result = result.Substring(0, result.Length - 1).TrimEnd();
                }
                else
                {
                    break;
                }
            }
            return result;
        }

        private static string CapitalizeFirst(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpper(text[0], CultureInfo.InvariantCulture) + (text.Length > 1 ? text.Substring(1) : "");
        }

        private static Dictionary<string, int> BuildPhaseIndexMap()
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < PhaseOrder.Length; i++)
            {
                map[PhaseOrder[i]] = i;
            }
            return map;
        }

        private class PhaseComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                int ix;
                int iy;
                if (!PhaseIndexMap.TryGetValue(x, out ix)) ix = int.MaxValue;
                if (!PhaseIndexMap.TryGetValue(y, out iy)) iy = int.MaxValue;
                return ix.CompareTo(iy);
            }
        }

        private static string JsonEscape(string text)
        {
            if (text == null) return "";
            StringBuilder sb = new StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
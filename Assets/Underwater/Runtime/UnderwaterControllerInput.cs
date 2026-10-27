using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace UnderwaterBaseline
{
    // Input System covers PICO native and Live Preview; legacy XR features remain a fallback.
    public sealed class UnderwaterControllerInput : System.IDisposable
    {
        readonly InputAction left = Axis("LeftHand"), right = Axis("RightHand");
        readonly InputAction a = Button("RightHand", "primaryButton"), b = Button("RightHand", "secondaryButton");
        readonly InputAction x = Button("LeftHand", "primaryButton"), y = Button("LeftHand", "secondaryButton");
        readonly InputAction lClick = Button("LeftHand", "thumbstickClicked"), rClick = Button("RightHand", "thumbstickClicked");
        readonly InputAction lTrigger = Button("LeftHand", "trigger"), rTrigger = Button("RightHand", "trigger");
        readonly InputAction lGrip = Button("LeftHand", "gripPressed"), rGrip = Button("RightHand", "gripPressed");
        public Vector2 Move, Turn;
        public bool Up, Down, Bubbles, Quality, Compare;
        public bool LightPressed, WakePressed, BubblePressed, QualityPressed, AreaPressed, RadarPressed, SoundTestPressed, MutePressed, PanelPressed;
        public bool LeftTrigger, RightTrigger, LeftGrip, RightGrip, TeleportHeld;
        public struct State
        {
            public Vector2 leftStick,rightStick;
            public bool a,b,x,y,leftClick,rightClick,leftTrigger,rightTrigger,leftGrip,rightGrip;
        }
        State previous;
        bool gripPending,yPending,chordUsed;
#if UNITY_EDITOR
        // Used only by the automated Editor Play check; never present in builds.
        public State? EditorInputOverride;
#endif
        public string Status { get; private set; } = "Controllers: waiting";
        static InputAction Axis(string hand)
        {
            var a = new InputAction(hand + "Stick", InputActionType.Value, expectedControlType: "Vector2");
            a.AddBinding("<XRController>{" + hand + "}/primary2DAxis");
            a.AddBinding("<XRController>{" + hand + "}/thumbstick");
            a.Enable(); return a;
        }
        static InputAction Button(string hand, string control)
        {
            var a = new InputAction(hand + control, InputActionType.Button, "<XRController>{" + hand + "}/" + control);
            if (control == "thumbstickClicked") a.AddBinding("<XRController>{" + hand + "}/primary2DAxisClick");
            a.Enable(); return a;
        }
        public void Read()
        {
#if UNITY_EDITOR
            if(EditorInputOverride.HasValue){ApplyState(EditorInputOverride.Value);Status="Editor control regression snapshot";return;}
#endif
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand); var r = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            ApplyState(new State{
                leftStick=l.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 lm)?lm:left.ReadValue<Vector2>(),
                rightStick=r.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 rm)?rm:right.ReadValue<Vector2>(),
                a=ReadButton(a,r,CommonUsages.primaryButton),b=ReadButton(b,r,CommonUsages.secondaryButton),
                x=ReadButton(x,l,CommonUsages.primaryButton),y=ReadButton(y,l,CommonUsages.secondaryButton),
                leftClick=ReadButton(lClick,l,CommonUsages.primary2DAxisClick),rightClick=ReadButton(rClick,r,CommonUsages.primary2DAxisClick),
                leftTrigger=ReadButton(lTrigger,l,CommonUsages.triggerButton),rightTrigger=ReadButton(rTrigger,r,CommonUsages.triggerButton),
                leftGrip=ReadGrip(lGrip,l),rightGrip=ReadGrip(rGrip,r)});
            Status = $"L {(left.activeControl?.device.displayName ?? (l.isValid ? l.name : "disconnected"))} {Move:F2}\nR {(right.activeControl?.device.displayName ?? (r.isValid ? r.name : "disconnected"))} {Turn:F2}";
        }
        // One snapshot per frame, one meaning per physical button, independent of radar state.
        public void ApplyState(State s)
        {
            Move=s.leftStick;Turn=s.rightStick;Up=s.a;Down=s.b;Compare=s.x;Bubbles=s.y;Quality=s.leftClick;
            LeftTrigger=s.leftTrigger;RightTrigger=s.rightTrigger;LeftGrip=s.leftGrip;RightGrip=s.rightGrip;TeleportHeld=s.rightClick;
            LightPressed=s.a&&!previous.a;WakePressed=s.b&&!previous.b;AreaPressed=s.x&&!previous.x;
            // Defer the two single-key actions until release so either order of the
            // requested chord cannot also fire bubbles or toggle the radar.
            PanelPressed=false;BubblePressed=false;RadarPressed=false;
            if(s.leftGrip&&!previous.leftGrip)gripPending=true;
            if(s.y&&!previous.y)yPending=true;
            if(s.leftGrip && s.y && !chordUsed){PanelPressed=true;chordUsed=true;gripPending=yPending=false;}
            if(!s.leftGrip && previous.leftGrip){RadarPressed=gripPending&&!chordUsed;gripPending=false;}
            if(!s.y && previous.y){BubblePressed=yPending&&!chordUsed;yPending=false;}
            if(!s.leftGrip && !s.y)chordUsed=false;
            QualityPressed=s.leftClick&&!previous.leftClick;
            SoundTestPressed=s.rightTrigger&&!previous.rightTrigger;MutePressed=s.leftTrigger&&!previous.leftTrigger;
            previous=s;
        }
        // Left stick: up/down = world height; left/right = approach/retreat.
        public static Vector3 MapMovement(Vector2 stick)
        {return Vector3.ClampMagnitude(new Vector3(0,stick.y,-stick.x),1);}
        static bool ReadButton(InputAction a, UnityEngine.XR.InputDevice device, InputFeatureUsage<bool> feature)
        { return device.TryGetFeatureValue(feature,out bool pressed)?pressed:a.IsPressed(); }
        static bool ReadGrip(InputAction action,UnityEngine.XR.InputDevice device)
        {
            bool hasDigital=device.TryGetFeatureValue(CommonUsages.gripButton,out bool pressed);
            bool hasAnalog=device.TryGetFeatureValue(CommonUsages.grip,out float amount);
            return hasDigital || hasAnalog?pressed || amount>=.5f:action.IsPressed();
        }
        public void Dispose() { foreach (var action in new[] {left,right,a,b,x,y,lClick,rClick,lTrigger,rTrigger,lGrip,rGrip}) action.Dispose(); }
    }
}

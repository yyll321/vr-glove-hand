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
        public bool Up, Down, Surface, Bubbles, Quality, Compare;
        public bool LeftTrigger, RightTrigger, LeftGrip, RightGrip, TeleportHeld;
        public bool RadarModifier => LeftGrip && UnderwaterGeometryRadar.ActiveForInput;
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
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand); var r = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            Move = left.ReadValue<Vector2>(); Turn = right.ReadValue<Vector2>();
            if (l.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 lm) && lm.sqrMagnitude > Move.sqrMagnitude) Move = lm;
            if (r.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 rm) && rm.sqrMagnitude > Turn.sqrMagnitude) Turn = rm;
            Up = ReadButton(a, r, CommonUsages.primaryButton); Down = ReadButton(b, r, CommonUsages.secondaryButton);
            Surface = false; Bubbles = ReadButton(y, l, CommonUsages.secondaryButton);
            Quality = ReadButton(lClick, l, CommonUsages.primary2DAxisClick); Compare = ReadButton(x,l,CommonUsages.primaryButton);
            LeftTrigger = ReadButton(lTrigger,l,CommonUsages.triggerButton);
            RightTrigger = ReadButton(rTrigger,r,CommonUsages.triggerButton);
            LeftGrip=ReadButton(lGrip,l,CommonUsages.gripButton);
            if(l.TryGetFeatureValue(CommonUsages.grip,out float leftGrip))LeftGrip|=leftGrip>=0.5f;
            RightGrip=ReadButton(rGrip,r,CommonUsages.gripButton);
            if(r.TryGetFeatureValue(CommonUsages.grip,out float grip))RightGrip|=grip>=0.5f;
            TeleportHeld=ReadButton(rClick,r,CommonUsages.primary2DAxisClick);
            Status = $"L {(left.activeControl?.device.displayName ?? (l.isValid ? l.name : "disconnected"))} {Move:F2}\nR {(right.activeControl?.device.displayName ?? (r.isValid ? r.name : "disconnected"))} {Turn:F2}";
        }
        // Left stick: up/down = world height; left/right = approach/retreat.
        public static Vector3 MapMovement(Vector2 stick)
        {return Vector3.ClampMagnitude(new Vector3(0,stick.y,-stick.x),1);}
        static bool ReadButton(InputAction a, UnityEngine.XR.InputDevice device, InputFeatureUsage<bool> feature)
        { device.TryGetFeatureValue(feature, out bool pressed); return a.IsPressed() || pressed; }
        public void Dispose() { foreach (var action in new[] {left,right,a,b,x,y,lClick,rClick,lTrigger,rTrigger,lGrip,rGrip}) action.Dispose(); }
    }
}

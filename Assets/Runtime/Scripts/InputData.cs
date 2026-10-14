//Struct InputData is a struct that contains all of the button, finger, and linear inputs. This is what's sent to the driver via the named pipe.
public struct InputData
{

    public float joyX; //range: -1 -> 1
    public float joyY; //range: -1 -> 1
    public bool joyButton;
    public bool trgButton;
    public bool aButton;
    public bool bButton;
    public bool grab;
    //public bool pinch;
    public bool menu;
    //public bool calibrate;
    public bool trackpad_touch;
    public float trgValue; //range: 0 -> 1

    //constructor that uses a 1d array for flexion.
    public InputData(float joyX, float joyY, bool joyButton, bool trgButton,
        bool aButton, bool bButton, bool grab, bool pinch, bool menu, bool calibrate, float trgValue,bool trackpad_touch)
    {
        this.joyX = joyX;
        this.joyY = joyY;
        this.joyButton = joyButton;
        this.trgButton = trgButton;
        this.aButton = aButton;
        this.bButton = bButton;
        this.grab = grab;
        //this.pinch = pinch;
        this.menu = menu;
        //this.calibrate = calibrate;
        this.trgValue = trgValue;
        this.trackpad_touch = trackpad_touch;
    }
}

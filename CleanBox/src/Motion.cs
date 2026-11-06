using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CleanBox {
    // A damped spring changes only presentation values; it never raises input events.
    internal sealed class SpringValue {
        public double Value,Target,Velocity;
        readonly double stiffness,damping;
        public SpringValue(double value,double stiffness=390,double damping=25){Value=Target=value;this.stiffness=stiffness;this.damping=damping;}
        public void Snap(double value){Value=Target=value;Velocity=0;}
        public bool Step(double seconds){
            double dt=Math.Min(.032,Math.Max(.001,seconds))/2;
            for(int i=0;i<2;i++){Velocity+=(stiffness*(Target-Value)-damping*Velocity)*dt;Value+=Velocity*dt;}
            if(Math.Abs(Target-Value)<.0005&&Math.Abs(Velocity)<.005){Snap(Target);return false;}
            return true;
        }
    }
    internal interface IMotionVisual {
        Control MotionControl {get;}
        bool Advance(double seconds);
        void Settle();
    }
    internal static class MotionClock {
        static readonly HashSet<IMotionVisual> active=new HashSet<IMotionVisual>();
        static readonly Timer timer=new Timer{Interval=15};
        static readonly Stopwatch watch=new Stopwatch();static double previous;
        static MotionClock(){timer.Tick+=(s,e)=>Tick();}
        internal static int ActiveCount{get{return active.Count;}}
        public static void Wake(IMotionVisual visual){
            if(!Palette.Animate){visual.Settle();return;}
            active.Add(visual);if(!timer.Enabled){previous=0;watch.Restart();timer.Start();}
        }
        public static void Remove(IMotionVisual visual){active.Remove(visual);if(active.Count==0){timer.Stop();watch.Stop();}}
        public static void FinishAll(){foreach(var visual in active.ToArray())visual.Settle();active.Clear();timer.Stop();watch.Stop();}
        static void Tick(){
            double now=watch.Elapsed.TotalSeconds,dt=now-previous;previous=now;
            foreach(var visual in active.ToArray()){
                Control c=visual.MotionControl;
                if(c.IsDisposed||!c.Visible||!Palette.Animate){if(!c.IsDisposed)visual.Settle();active.Remove(visual);continue;}
                if(!visual.Advance(dt))active.Remove(visual);
            }
            if(active.Count==0){timer.Stop();watch.Stop();}
        }
    }
}

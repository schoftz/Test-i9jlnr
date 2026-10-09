// Headless fizik testi: oyundaki CarMath ile 1B hızlanma + devrilme kontrolü.
// Derleme: mcs PhysicsSim.cs ../../Assets/Scripts/CarMath.cs ../../Assets/ThirdParty/RVP/RvpTire.cs && mono PhysicsSim.exe
using System; using MostWanted;
class PhysicsSim {
  struct Car { public string n; public float m, tq, red, top; public int drive; public float grip, track, com; }
  static void Main() {
    Car[] cars = {
      new Car{n="Kompakt Hatch",m=1200,tq=260,red=6800,top=205,drive=0,grip=1.00f,track=1.55f},
      new Car{n="Tuner S",m=1350,tq=400,red=7800,top=245,drive=2,grip=1.05f,track=1.6f},
      new Car{n="Muscle V8",m=1650,tq=620,red=6500,top=265,drive=1,grip=0.97f,track=1.65f},
      new Car{n="Street GT-R",m=1500,tq=580,red=8000,top=290,drive=2,grip=1.12f,track=1.62f},
      new Car{n="Süper Kanat",m=1450,tq=650,red=8800,top=315,drive=1,grip=1.16f,track=1.7f},
      new Car{n="Bulldog SUV",m=2100,tq=650,red=6200,top=235,drive=2,grip=0.95f,track=1.7f},
    };
    float r = 0.34f, dt = 0.005f;
    Console.WriteLine("{0,-14} {1,8} {2,9} {3,9} {4,10} {5,10}", "Araç", "0-100 s", "Vmax km/s", "dir.13km", "max yanal", "iç tek. yük");
    foreach (var c in cars) {
      float fd = CarMath.FinalDrive(c.top, c.red, r), k = CarMath.DragCoef(c.tq, c.top, c.red, r);
      float v = 0, t = 0, t100 = -1; int gear = 1; float shift = 0;
      // tahrik aksı yükü (statik ~%50) * sürtünme 1.5 * tutuş → çekiş sınırı
      float axle = c.drive == 2 ? 1f : 0.5f;
      float tract = c.m * 9.81f * axle * 1.5f * c.grip * 0.75f; // asimptot/ekstremum ortalaması
      for (int i = 0; i < 200000 && t < 60; i++) {
        float rpm = Math.Max(CarMath.IdleRpm, CarMath.EngineRpm(v, gear, fd, r));
        if (gear == 1 && v * 3.6f < 25f) rpm = Math.Max(rpm, CarMath.IdleRpm + c.red * 0.55f);
        if (rpm > c.red * 0.96f && gear < 6 && shift <= 0) { gear++; shift = 0.28f; }
        float F = shift > 0 ? 0 : CarMath.WheelForce(c.tq * CarMath.TorqueCurve(Math.Min(rpm, c.red) / c.red), gear, fd, r);
        F = Math.Min(F, tract);
        float a = (F - k * v * v - (v > 0.5f ? c.m * CarMath.RollingDecel : 0)) / c.m;
        v += a * dt; t += dt; shift -= dt;
        if (t100 < 0 && v * 3.6f >= 100) t100 = t;
      }
      // devrilme: max yanal ivme = tutuş(1.4*grip) g; kuvvet uygulama noktası COM-0.08 m
      float lat = 1.4f * c.grip * 9.81f, com = 0.36f, app = com - 0.08f;
      float inner = CarMath.InnerWheelLoadRatio(c.m, lat, com, app, c.track, 0.3f);
      float steer13 = RVP.RvpTire.SteerCurve(13f / 3.6f) * 40f;
      Console.WriteLine("{0,-14} {1,8:0.0} {2,9:0} {3,9:0.0}° {4,9:0.00}g {5,10:0.00}", c.n, t100, v * 3.6f, steer13, lat / 9.81f, inner);
    }
    Console.WriteLine("Direksiyon sınırı: 0→{0:0.0}°, 60→{1:0.0}°, 150→{2:0.0}°, 250→{3:0.0}°", RVP.RvpTire.SteerCurve(0f/3.6f)*40f, RVP.RvpTire.SteerCurve(60f/3.6f)*40f, RVP.RvpTire.SteerCurve(150f/3.6f)*40f, RVP.RvpTire.SteerCurve(250f/3.6f)*40f);
  }
}

// Gerçek araç verileri ile 0-100 ve azami hız (oyundaki CarMath, araç başına vites sayısı).
// Derleme: mcs RealCarsSim.cs ../../Assets/Scripts/CarMath.cs && mono RealCarsSim.exe
using System; using MostWanted;
class C { public string n; public float m, tq, red, top; public int drive; public float grip; public int gears; public float real100;
  public C(string n, float m, float tq, float red, float top, int drive, float grip, int gears, float real100) { this.n=n; this.m=m; this.tq=tq; this.red=red; this.top=top; this.drive=drive; this.grip=grip; this.gears=gears; this.real100=real100; } }
class RealCarsSim {
  static void Main() {
    var cars = new C[] {
      new C("Mini JCW", 1300f, 320f, 6500f, 246f, 0, 1.00f, 8, 6.1f),
      new C("Dodge Hellcat", 1950f, 889f, 6200f, 315f, 1, 1.15f, 8, 3.6f),
      new C("Ford F-150 Raptor", 2600f, 691f, 6000f, 180f, 2, 0.95f, 10, 5.5f),
      new C("BMW M4 Comp", 1725f, 650f, 7200f, 290f, 1, 1.12f, 8, 3.9f),
      new C("Tesla Cybertruck", 3100f, 1500f, 16000f, 209f, 2, 1.05f, 1, 2.7f),
      new C("Nissan GT-R R35", 1752f, 633f, 7100f, 315f, 2, 1.18f, 6, 2.9f),
      new C("Toyota Supra A80", 1510f, 441f, 6800f, 285f, 1, 1.08f, 6, 4.6f),
      new C("Porsche 911 TS", 1640f, 800f, 7200f, 330f, 2, 1.22f, 8, 2.7f),
      new C("Nissan R34 GT-R", 1560f, 360f, 8000f, 265f, 2, 0.95f, 6, 4.9f),
      new C("Bugatti Chiron", 1995f, 1600f, 6700f, 420f, 2, 1.10f, 7, 2.4f),
    };
    float r = 0.34f, dt = 0.002f;
    Console.WriteLine("{0,-18} {1,8} {2,8} {3,9} {4,9}", "Araç", "0-100 G", "0-100 S", "Vmax G", "Vmax S");
    foreach (var c in cars) {
      var rt = CarMath.MakeRatios(c.gears);
      float fd = CarMath.FinalDrive(c.top, c.red, r, rt), k = CarMath.DragCoef(c.tq, c.top, c.red, r, rt);
      float v = 0, t = 0, t100 = -1; int gear = 1; float shift = 0, since = 1;
      float share = c.drive == 2 ? 1f : c.drive == 1 ? 0.6f : 0.45f;   // kalkışta ağırlık arkaya kayar
      float tract = c.m * 9.81f * share * 1.5f * c.grip * 0.85f;
      while (t < 90f) {
        float kmh = v * 3.6f;
        float rpm = Math.Max(CarMath.IdleRpm, CarMath.EngineRpm(v, gear, fd, r, rt));
        if (gear == 1 && kmh < 30f) rpm = Math.Max(rpm, CarMath.IdleRpm + Math.Min(c.red * 0.55f, 4300f));
        since += dt;
        if (rpm > c.red * 0.96f && gear < rt.Length && since > 0.45f) { gear++; shift = 0.15f; since = 0; }
        float T = c.tq * CarMath.TorqueCurve(Math.Min(rpm, c.red) / c.red);
        if (gear == 1) T *= CarMath.LaunchAssist(kmh);
        float F = shift > 0 ? 0 : Math.Min(CarMath.WheelForce(T, gear, fd, r, rt), tract);
        float a = (F - k * v * v - (v > 0.5f ? c.m * CarMath.RollingDecel : 0)) / c.m;
        v += a * dt; t += dt; shift -= dt;
        if (t100 < 0 && kmh >= 100) t100 = t;
        if (kmh > c.top * 1.08f) break;
      }
      Console.WriteLine("{0,-18} {1,7:0.0}s {2,7:0.0}s {3,6:0}km/s {4,6:0}km/s", c.n, c.real100, t100, c.top, v * 3.6f);
    }
  }
}

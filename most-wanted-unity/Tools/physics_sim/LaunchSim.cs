// Kalkış testi: 0–50 km/sa, %0 ve %12 eğim (oyundaki CarMath ile).
// Derleme: mcs LaunchSim.cs ../../Assets/Scripts/CarMath.cs ../../Assets/ThirdParty/RVP/RvpTire.cs && mono LaunchSim.exe
using System; using MostWanted;
class LaunchSim {
  static float Run(float m, float tq, float red, float top, int drive, float grip, double slopePct, bool assists) {
    float r = 0.34f, dt = 0.002f, g = 9.81f;
    double th = Math.Atan(slopePct / 100.0); float sin = (float)Math.Sin(th), cos = (float)Math.Cos(th);
    float fd = CarMath.FinalDrive(top, red, r), k = CarMath.DragCoef(tq, top, red, r);
    // yokuşta yük arkaya kayar: arkadan itişte biraz artar, önden çekişte azalır
    float shift = 0.5f * sin * 0.55f / 2.6f; float share = drive == 2 ? 1f : drive == 1 ? 0.5f + shift : 0.5f - shift;
    float tract = m * g * cos * share * 1.5f * grip * 0.85f;
    float v = 0, t = 0; int gear = 1;
    while (v * 3.6f < 50f && t < 30f) {
      float kmh = v * 3.6f;
      float rpm = Math.Max(CarMath.IdleRpm, CarMath.EngineRpm(v, gear, fd, r));
      if (gear == 1 && kmh < 30f) rpm = Math.Max(rpm, CarMath.IdleRpm + Math.Min(red * 0.55f, 4300f));
      if (rpm > red * 0.96f && gear < 6) gear++;
      float T = tq * CarMath.TorqueCurve(Math.Min(rpm, red) / red);
      if (assists && gear == 1) T *= CarMath.LaunchAssist(kmh);
      float F = Math.Min(CarMath.WheelForce(T, gear, fd, r), tract);
      float hill = assists ? m * g * sin * CarMath.HillAssist(kmh) : 0f;
      float a = (F + hill - m * g * sin - k * v * v - (v > 0.5f ? m * CarMath.RollingDecel : 0)) / m;
      v = Math.Max(0, v + a * dt); t += dt;
    }
    return t;
  }
  static void Main() {
    var cars = new[] { ("Kompakt Hatch", 1200f, 260f, 6800f, 205f, 0, 1.0f), ("Tuner S", 1350f, 400f, 7800f, 245f, 2, 1.05f), ("Muscle V8", 1650f, 620f, 6500f, 265f, 1, 0.97f), ("Bulldog SUV", 2100f, 650f, 6200f, 235f, 2, 0.95f) };
    Console.WriteLine("{0,-14} {1,10} {2,10} {3,14}", "Araç", "0-50 %0", "0-50 %12", "%12 yardımsız");
    foreach (var c in cars)
      Console.WriteLine("{0,-14} {1,9:0.00}s {2,9:0.00}s {3,13:0.00}s", c.Item1, Run(c.Item2, c.Item3, c.Item4, c.Item5, c.Item6, c.Item7, 0, true), Run(c.Item2, c.Item3, c.Item4, c.Item5, c.Item6, c.Item7, 12, true), Run(c.Item2, c.Item3, c.Item4, c.Item5, c.Item6, c.Item7, 12, false));
    // dönüş yarıçapı (30 km/sa, tam direksiyon): R = dingil / tan(açı); dingil ≈ araç boyu × 0.6
    Console.WriteLine();
    Console.WriteLine("{0,-14} {1,8} {2,10} {3,10}", "Araç", "boy m", "açı@30", "R@30 m");
    foreach (var c in new[] { ("Kompakt Hatch", 4.1f), ("Sokak Coupe", 4.3f), ("Tuner S", 4.4f), ("Coupe RS", 4.45f), ("Bulldog SUV", 4.9f), ("Titan Pikap", 5.0f), ("Muscle V8", 4.8f), ("Drift Spec", 4.4f), ("Street GT-R", 4.5f), ("Süper Kanat", 4.55f) })
    {
      float wb = c.Item2 * 0.6f, ang = RVP.RvpTire.SteerCurve(30f / 3.6f) * 40f;
      float R = wb / (float)Math.Tan(ang * Math.PI / 180.0);
      Console.WriteLine("{0,-14} {1,8:0.00} {2,9:0.0}° {3,9:0.0}{4}", c.Item1, c.Item2, ang, R, R < 12f ? "  OK" : "  BÜYÜK!");
    }
  }
}

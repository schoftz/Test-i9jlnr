// Dinamik direksiyon testi: bisiklet modeli + WheelFrictionCurve benzeri lastik + oyundaki CarMath
// (SteerLimit, YawAssistTorque). Sabit hızda tam direksiyon 3 sn → dönüş yarıçapı ve yanal ivme.
// Derleme: mcs SteerSim.cs ../../Assets/Scripts/CarMath.cs && mono SteerSim.exe
using System; using MostWanted;
class SteerSim {
  // Unity WheelFrictionCurve (ext 0.22/1.0, asym 0.6/0.72) yaklaşımı
  static float Curve(float s) {
    s = Math.Abs(s);
    if (s < 0.22f) { float t = s / 0.22f; return t * (2f - t); }
    if (s < 0.6f) { float t = (s - 0.22f) / 0.38f; t = t * t * (3 - 2 * t); return 1f + (0.72f - 1f) * t; }
    return 0.72f;
  }
  static void Run(string name, float m, float len, float grip, float down, float kmh, bool oldModel, out float R, out float ay) {
    float L = len * 0.6f, a = L * 0.47f, b = L * 0.53f, W = 1.9f, g = 9.81f;
    float Iz = m * (W * W + len * len) / 12f;
    float vx = kmh / 3.6f, vy = 0, r = 0, dt = 0.002f, steerDeg = 0;
    float limit = oldModel ? OldSteer(kmh) : CarMath.SteerLimit(kmh, 38f);
    float kf = (oldModel ? 1.4f : CarMath.SideStiffness(true, kmh)) * grip, kr = (oldModel ? 1.35f : CarMath.SideStiffness(false, kmh)) * grip;
    float rate = oldModel ? (90f + kmh * 0.4f) : CarMath.SteerRate(false);
    for (float t = 0; t < 3f; t += dt) {
      steerDeg = Math.Min(limit, steerDeg + rate * dt);
      float d = steerDeg * (float)Math.PI / 180f;
      float N = (m * g + 0.85f * down * vx * vx) * 0.5f;            // aks başına yük
      float sf = (float)Math.Atan2(vy + a * r, vx) - d, sr = (float)Math.Atan2(vy - b * r, vx);
      float Ff = -Math.Sign(sf) * Curve(sf) * kf * N;
      float Fr = -Math.Sign(sr) * Curve(sr) * kr * N;
      float assist;
      if (oldModel) { float des = vx * (float)Math.Tan(d) / L; float ex = r - des; assist = (kmh > 35f && Math.Abs(ex) > 0.25f) ? -ex * Iz * 0.45f * 4f : 0f; }
      else assist = CarMath.YawAssistTorque(vx, steerDeg, r, L, Iz, 1f, 1.3f * grip);
      float dvy = (Ff * (float)Math.Cos(d) + Fr) / m - vx * r;
      float dr = (a * Ff * (float)Math.Cos(d) - b * Fr + assist) / Iz;
      vy += dvy * dt; r += dr * dt;
    }
    // dönüş yarıçapı kütle merkezi hızından; spin: yanal hız ileri hızın %40'ından büyük
    float vtot = (float)Math.Sqrt(vx * vx + vy * vy);
    R = Math.Abs(r) > 1e-3f ? vtot / Math.Abs(r) : 9999f; ay = vtot * Math.Abs(r) / g;
    if (Math.Abs(vy) > 0.4f * vx) { R = -1f; }
  }
  static float OldSteer(float kmh) { float t = Math.Min(1f, kmh / 150f); float s = t * t * (3 - 2 * t); return 32f + (7f - 32f) * s; }
  static void Main() {
    var cars = new[] { new { n = "Mini JCW", m = 1300f, l = 3.9f, g = 1.0f, d = 0.8f }, new { n = "Dodge Hellcat", m = 1950f, l = 5.0f, g = 1.15f, d = 0.8f },
      new { n = "Ford Raptor", m = 2600f, l = 5.9f, g = 0.95f, d = 0.5f }, new { n = "BMW M4", m = 1725f, l = 4.8f, g = 1.12f, d = 1.0f },
      new { n = "Cybertruck", m = 3100f, l = 5.7f, g = 1.05f, d = 0.6f }, new { n = "GT-R R35", m = 1752f, l = 4.7f, g = 1.18f, d = 1.2f },
      new { n = "Supra A80", m = 1510f, l = 4.5f, g = 1.08f, d = 1.0f }, new { n = "911 Turbo S", m = 1640f, l = 4.55f, g = 1.22f, d = 1.3f },
      new { n = "R34 GT-R", m = 1560f, l = 4.6f, g = 0.98f, d = 1.1f }, new { n = "Chiron", m = 1995f, l = 4.55f, g = 1.12f, d = 1.6f } };
    Console.WriteLine("{0,-14} | {1,-22} | {2,-22} | {3,-22}", "Araç", "30 km/s R (eski→yeni)", "100 km/s R (eski→yeni)", "100 km/s yanal g (eski→yeni)");
    bool allOk = true;
    foreach (var c in cars) {
      float r30o, a30o, r30, a30, r100o, a100o, r100, a100;
      Run(c.n, c.m, c.l, c.g, c.d, 30, true, out r30o, out a30o); Run(c.n, c.m, c.l, c.g, c.d, 30, false, out r30, out a30);
      Run(c.n, c.m, c.l, c.g, c.d, 100, true, out r100o, out a100o); Run(c.n, c.m, c.l, c.g, c.d, 100, false, out r100, out a100);
      bool ok = r30 > 0 && r30 <= 8f && r100 > 0 && r100 <= 90f && a100 >= 0.9f; allOk &= ok;
      Func<float, string> F = x => x < 0 ? " SPIN" : x.ToString("0.0").PadLeft(5);
      Console.WriteLine("{0,-14} | {1} → {2} m      | {3} → {4} m      | {5,6:0.00} → {6,4:0.00} g   {7}", c.n, F(r30o), F(r30), F(r100o), F(r100), r100o < 0 ? 0 : a100o, a100, ok ? "OK" : "HEDEF DIŞI");
    }
    Console.WriteLine(allOk ? "Tüm araçlar hedefte (30 km/s R ≤ 8 m, 100 km/s R ≤ 90 m ve ≥ 0.9 g)." : "Bazı araçlar hedef dışında!");
  }
}

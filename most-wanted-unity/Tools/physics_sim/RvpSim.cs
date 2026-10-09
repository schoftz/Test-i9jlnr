// Normal mod (RVP) direksiyon testi: düzlemsel 4 tekerlek modeli, oyundaki RvpTire fonksiyonları ve
// RvpWheel.ApplyFriction ile aynı mantık (TCS, kayma bağımlılığı, sürtünme yumuşatma, RVP savrulma yardımı).
// 30/60/100 km/sa'da tam direksiyon, gaz 0 ve gaz 1 → dönüş yarıçapı ve yanal ivme; el freni ve lift-off testi.
// Derleme: mcs RvpSim.cs ../../Assets/Scripts/CarMath.cs ../../Assets/ThirdParty/RVP/RvpTire.cs && mono RvpSim.exe
using System; using MostWanted; using RVP;
class RvpSim {
  const float g = 9.81f, dt = 1f / 60f;
  static float m = 1500, L = 2.55f, a = 1.25f, b = 1.30f, T = 1.56f, h = 0.45f, rW = 0.34f, mu = 1.05f;
  static float peakT = 550, redline = 7000, top = 250; static int drive = 1;
  static float[] ratios = CarMath.MakeRatios(6); static float fd, dragK;
  static float[] F = new float[4], S = new float[4];       // yumuşatılmış sürtünme (m/s²)
  static float Iz;

  class St { public float u, w, r, steer, x, y, psi; public int gear = 1; }

  static void Step(St s, float steerIn, float thr, bool hb, float assist) {
    float kmh = (float)Math.Sqrt(s.u * s.u + s.w * s.w) * 3.6f;
    // RVP SteeringControl
    float limit = Math.Min(44f, 40f * (arcade ? RvpTire.SteerCurveArcade(s.u) : RvpTire.SteerCurve(s.u)) * sens); float muS = mu * (arcade ? RvpTire.ArcadeGrip(kmh) * 1.4f : 1.4f);
    limit = Math.Min(limit, RvpTire.SteerOptimal(s.u, L, muS) * (0.95f + 0.25f * sens));
    if (hb) limit = Math.Max(limit, 24f);
    float tgt = steerIn * limit; bool ret = Math.Abs(tgt) < Math.Abs(s.steer);
    float stp = RvpTire.SteerRateDeg(40f, ret, sens) * dt; s.steer += Math.Max(-stp, Math.Min(stp, tgt - s.steer));
    // şanzıman (basit otomatik)
    float rpm = CarMath.EngineRpm(s.u, s.gear, fd, rW, ratios);
    if (rpm > redline * 0.96f && s.gear < ratios.Length) s.gear++;
    if (rpm < redline * 0.45f && s.gear > 1) s.gear--;
    rpm = Math.Max(rpm, CarMath.IdleRpm);
    float engT = thr > 0.05f ? peakT * CarMath.TorqueCurve(Math.Min(1f, rpm / redline)) * thr * (s.gear == 1 ? CarMath.LaunchAssist(kmh) : 1f) : 0f;
    float wheelT = engT * CarMath.GearRatio(s.gear, ratios) * fd * CarMath.Efficiency;
    float brkT = thr < 0.05f ? m * g * rW * 0.42f * 0.04f : 0f;
    // yükler (yarı-statik transfer) — önceki adımın ivmelerinden
    float fx = 0, fy = 0, mz = 0;
    float[] pf = { a, a, -b, -b }, ps = { -T / 2, T / 2, -T / 2, T / 2 };
    float ax = lastAx, ay = lastAy;
    for (int i = 0; i < 4; i++) {
      bool front = i < 2;
      float N = m * g / 4f + (front ? -1 : 1) * m * ax * h / L / 2f + (ps[i] > 0 ? 1 : -1) * m * ay * h / T / 2f;
      N = Math.Max(0f, N);
      float d = front ? s.steer * (float)Math.PI / 180f : 0f;
      float vxp = s.u - s.r * ps[i], vyp = s.w + s.r * pf[i];
      float cd = (float)Math.Cos(d), sd = (float)Math.Sin(d);
      float vx = vxp * cd + vyp * sd, vz = -vxp * sd + vyp * cd;
      float limitA = mu * N / m, peak = Math.Max(0.01f, limitA);
      float drv = wheelT * (front ? 0f : 0.5f) / rW / m;
      float bA = brkT * (front ? 1.2f : 0.8f) / rW / m;
      float req = drv; if (bA > 0) req -= Math.Sign(vx) * Math.Min(bA, Math.Abs(vx) / dt);
      bool tcs = kmh > 15f;
      if (tcs) req = Math.Max(-peak * 0.98f, Math.Min(peak * 0.98f, req));
      float ratio = Math.Abs(req) / peak;
      float spin = (!front && hb) ? 1f : -1f;
      float fwdSlip = spin >= 0 ? spin : ratio <= 1 ? ratio * 0.2013f : 0.2013f + (ratio - 1) * 0.8f;
      float Fx = Math.Sign(req) * Math.Min(Math.Abs(req), RvpTire.FwdCurve(fwdSlip) * peak);
      if (spin >= 0) Fx = -Math.Sign(vx) * Math.Min(Math.Abs(vx) / dt, RvpTire.FwdCurve(fwdSlip) * peak * 0.35f);
      float gF, gR; RvpTire.SideGrip(kmh, out gF, out gR); if (arcade) { float ag = RvpTire.ArcadeGrip(kmh); gF *= hb ? 1f : ag; gR *= ag; } float sideMul = front ? gF * (1f + 0.05f * lift) : gR * (1f - 0.04f * lift);   // arka 1.15: kararlılık (oyundaki RvpStep ile aynı)
      if (!front && hb) sideMul *= 0.75f;
      float dep = RvpTire.Dependence(1.6f, Math.Max(0f, Math.Min(1f, fwdSlip / 0.2013f - 1f)));
      float Fz = -Math.Sign(vz) * RvpTire.SideCurve(RvpTire.SideSlip(vz)) * peak * sideMul * dep;
      float k = RvpTire.SmoothFactor(0.5f, dt);
      F[i] += (Fx - F[i]) * k; S[i] += (Fz - S[i]) * k;
      float bx = F[i] * cd - S[i] * sd, by = F[i] * sd + S[i] * cd;   // gövde ekseninde (m/s²)
      fx += bx; fy += by; mz += (pf[i] * by - ps[i] * bx) * m;
    }
    float v2 = s.u * s.u;
    fx -= (dragK * v2 + (s.u > 0.5f ? m * CarMath.RollingDecel : 0f)) / m;
    float lv = s.w;
    float muEff = arcade ? mu * RvpTire.ArcadeGrip(kmh) * 1.3f : mu;
    float yawAcc = mz / Iz + RvpTire.SpinAssist(steerIn, s.u, lv, s.r, 2.2f, 1.6f * assist, s.steer, L, muEff);
    if (arcade && !hb) yawAcc += RvpTire.ArcadeTurnIn(steerIn, s.u, s.r, s.steer, L, muEff);
    lastAx = fx; lastAy = fy;
    s.u += (fx + s.r * s.w) * dt;
    s.w += (fy - s.r * s.u) * dt;
    s.r += yawAcc * dt;
    s.psi += s.r * dt;
    s.x += (s.u * (float)Math.Cos(s.psi) - s.w * (float)Math.Sin(s.psi)) * dt;
    s.y += (s.u * (float)Math.Sin(s.psi) + s.w * (float)Math.Cos(s.psi)) * dt;
  }
  static float lastAx, lastAy, lift; static bool arcade; static float sens = 1f;

  static St Start(float kmh) {
    Array.Clear(F, 0, 4); Array.Clear(S, 0, 4); lastAx = lastAy = 0; lift = 0;
    var s = new St { u = kmh / 3.6f };
    for (int gI = 1; gI <= 6; gI++) { s.gear = gI; if (CarMath.EngineRpm(s.u, gI, fd, rW, ratios) < redline * 0.85f) break; }
    return s;
  }

  static void Main() {
    fd = CarMath.FinalDrive(top, redline, rW, ratios);
    dragK = CarMath.DragCoef(peakT, top, redline, rW, ratios);
    Iz = m * (1.9f * 1.9f + 4.4f * 4.4f) / 12f;
    foreach (var mode in new[] { false, true }) {
      arcade = mode;
      Console.WriteLine("\n===== " + (arcade ? "ARCADE (oyuncu varsayılanı)" : "GERÇEKÇİ (RVP)") + " =====");
      foreach (float sv in new[] { 0.6f, 1.2f, 2.0f }) {
        sens = sv;
        Console.Write("hassasiyet {0:0.0}: sabit hız R =", sv);
        foreach (float kmh in new[] { 30f, 60f, 100f, 120f }) Console.Write("  {0:0}→{1:0.0} m", kmh, ConstR(kmh));
        Console.WriteLine();
      }
      sens = 1.2f;
      Run();
    }
  }
  static float ConstR(float kmh) {
    var s = Start(kmh); float rs = 0, n = 0;
    for (float t = 0; t < 2.5f; t += dt) {
      float thr = Math.Max(0f, Math.Min(1f, (kmh / 3.6f - s.u) * 0.8f + 0.2f));
      Step(s, 1f, thr, false, 1f);
      if (t > 2f) { rs += (float)Math.Sqrt(s.u * s.u + s.w * s.w) / Math.Max(1e-3f, s.r); n++; }
    }
    return rs / n;
  }
  static void Run() {
    Console.WriteLine("RVP normal mod — RWD 1500 kg, 550 Nm, μ=1.05; tam sağ direksiyon 2.0 sn (yarıçap = v/r, 1.5–2.0 sn ortalaması)");
    Console.WriteLine("hız   gaz | R (m)   v_son (km/s)  yanal (g)  kayma (°)  direksiyon (°)");
    foreach (float kmh in new[] { 30f, 60f, 100f, 120f }) {
      foreach (float thr in new[] { 0f, 1f }) {
        var s = Start(kmh); float rs = 0, vs = 0, ays = 0, n = 0;
        for (float t = 0; t < 2.0f; t += dt) {
          lift = thr < 0.05f && kmh > 40 ? Math.Min(1f, t * 3f) : 0f;
          Step(s, 1f, thr, false, 1f);
          if (t > 1.5f) { float v = (float)Math.Sqrt(s.u * s.u + s.w * s.w); rs += v / Math.Max(1e-3f, s.r); vs += v; ays += v * s.r; n++; }
        }
        float slip = (float)(Math.Atan2(s.w, s.u) * 180 / Math.PI);
        Console.WriteLine("{0,4:0}  {1,3:0} | {2,6:0.0}   {3,8:0.0}      {4,6:0.00}    {5,6:0.0}     {6,6:0.0}", kmh, thr, rs / n, vs / n * 3.6f, ays / n / g, slip, s.steer);
      }
    }
    // aynı hızda karşılaştırma: gaz 0 vs 1, hız sabit tutulan (gaz ayarlı) dönüş — saf direksiyon etkisi
    Console.WriteLine("\nSabit hız (gaz otomatik ayarlı) — tam direksiyon 2 sn:");
    foreach (float kmh in new[] { 30f, 60f, 100f, 120f }) {
      var s = Start(kmh); float rs = 0, n = 0, thrSum = 0;
      for (float t = 0; t < 2.0f; t += dt) {
        float thr = Math.Max(0f, Math.Min(1f, (kmh / 3.6f - s.u) * 0.8f + 0.2f));
        Step(s, 1f, thr, false, 1f);
        if (t > 1.5f) { rs += (float)Math.Sqrt(s.u * s.u + s.w * s.w) / s.r; n++; thrSum += thr; }
      }
      Console.WriteLine("{0,4:0} km/s: R={1:0.0} m, ort. gaz={2:0.00}", kmh, rs / n, thrSum / n);
    }
    // el freni: 60 km/s, tam direksiyon + el freni 1 sn
    { var s = Start(60); float maxSlip = 0; for (float t = 0; t < 1.2f; t += dt) { Step(s, 0.7f, 0.3f, t < 1f, 1f); maxSlip = Math.Max(maxSlip, (float)Math.Abs(Math.Atan2(s.w, s.u) * 180 / Math.PI)); }
      Console.WriteLine("\nEl freni 60 km/s (%70 direksiyon, 1 sn): maks. kayma açısı {0:0.0}° (drift), son hız {1:0} km/s", maxSlip, Math.Sqrt(s.u * s.u + s.w * s.w) * 3.6); }
    // lift-off: 100 km/s yarım direksiyonla gazda 2 sn, sonra gaz bırak 1 sn → savrulma hızı değişimi
    { var s = Start(100); float r0 = 0;
      for (float t = 0; t < 3f; t += dt) { bool off = t > 2f; lift = off ? Math.Min(1f, (t - 2f) * 3f) : 0f; Step(s, 0.5f, off ? 0f : 0.5f, false, 1f); if (Math.Abs(t - 2f) < dt / 2) r0 = s.r; }
      Console.WriteLine("Lift-off 100 km/s: savrulma hızı {0:0.000} → {1:0.000} rad/s (artış = hafif oversteer), kayma {2:0.0}°", r0, s.r, Math.Atan2(s.w, s.u) * 180 / Math.PI); }
  }
}

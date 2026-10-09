// RVP ışın-izli süspansiyon testi (oyundaki RvpTire.SpringAccel / HardContactAccel ile): yarım araç (dikey + yunuslama).
// 1) 5 cm yükseklikten bırakma → oturma süresi, aşım sayısı. 2) 100 km/s'de 2 cm basamak → havada kalan kare sayısı.
// Derleme: mcs SuspSim.cs ../../Assets/ThirdParty/RVP/RvpTire.cs && mono SuspSim.exe
using System; using RVP;
class SuspSim {
  const float g = 9.81f, dt = 1f / 60f, travel = 0.18f, m = 1500f, a = 1.25f, b = 1.30f;
  static float Iy = m * (4.4f * 4.4f) / 12f;
  // CarController.ApplySuspension ile aynı: f = 1.7 Hz, ζ = 0.5 (köşe kütlesi)
  static float corner = m / 4f, k = corner * (float)Math.Pow(2 * Math.PI * 1.7, 2), c = 2f * 0.5f * (float)Math.Sqrt(k * corner);
  static float springForce = k * travel / m, damp = c / (m * springForce);
  static float y, vy, th, w;   // gövde: bağlantı noktası yüksekliği (yerden), pitch
  static float Ground(float x, float stepX) { return x >= stepX ? 0.02f : 0f; }
  static int Step(float x, float stepX) {
    float fz = -m * g, my = 0; int grounded = 0;
    for (int i = 0; i < 2; i++) {
      float px = i == 0 ? a : -b;
      float h = y + px * th - Ground(x + px, stepX);        // bağlantı noktasının zemine yüksekliği (tekerlek yarıçapı hariç)
      float vrel = vy + px * w;
      if (h > travel) continue;
      grounded++;
      float comp = Math.Max(0f, Math.Min(1f, h / travel));
      float acc = RvpTire.SpringAccel(springForce, damp, comp, vrel);
      if (comp <= 0f) acc += RvpTire.HardContactAccel(vrel, Math.Min(0f, h));
      acc = Math.Max(0f, acc);
      float F = acc * m * 2f;                               // aks: 2 tekerlek
      fz += F; my += F * px;
    }
    vy += fz / m * dt; y += vy * dt; w += my / Iy * dt; th += w * dt;
    return grounded;
  }
  static void Main() {
    // dinlenme yüksekliği
    y = travel; vy = 0; th = 0; w = 0;
    for (int i = 0; i < 600; i++) Step(0, 1e9f);
    float rest = y; Console.WriteLine("Dinlenme: sıkışma oranı %{0:0} (yük altında), f=1.7 Hz, ζ=0.5", (1 - rest / travel) * 100);
    // 1) 5 cm bırakma
    y = rest + 0.05f; vy = 0; th = 0; w = 0;
    int over = 0; float lastSign = 1; float settle = -1;
    for (int i = 0; i < 180; i++) {
      Step(0, 1e9f);
      float e = y - rest; float s = e > 0.002f ? 1 : e < -0.002f ? -1 : 0;
      if (s != 0 && s != lastSign) { over++; lastSign = s; }
      if (Math.Abs(e) > 0.004f) settle = -1; else if (settle < 0) settle = i * dt;
    }
    Console.WriteLine("5 cm bırakma: ±4 mm içinde oturma {0:0.00} sn, aşım (±2 mm ötesi karşı tarafa geçiş) {1}", settle, over);
    // 2) 100 km/s 2 cm basamak
    y = rest; vy = 0; th = 0; w = 0;
    float v = 100f / 3.6f, x = 0; int air = 0, partial = 0; float maxDy = 0;
    for (int i = 0; i < 120; i++) { int gr = Step(x, 5f); x += v * dt; if (gr == 0) air++; else if (gr < 2) partial++; maxDy = Math.Max(maxDy, Math.Abs(y - rest - 0.02f * (x > 5 ? 1 : 0))); }
    Console.WriteLine("100 km/s, 2 cm basamak: tamamen havada {0} kare, bir aks havada {1} kare", air, partial);
  }
}

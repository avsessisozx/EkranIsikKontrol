using System;
using System.Threading;

namespace EkranIsikKontrol
{
    // Yavaş bir işi (kayıt yazma, fareye renk gönderme) arka planda yapar; arada gelen istekleri birleştirir,
    // yalnızca en son istenen değer uygulanır. Kaydırıcı sürüklenirken arayüz donmaz.
    public sealed class SonDegerIsci<T> : IDisposable
    {
        private readonly Action<T> is_;
        private readonly int aralikMs;
        private readonly object kilit = new object();
        private readonly AutoResetEvent sinyal = new AutoResetEvent(false);
        private readonly Thread thread;
        private bool bekliyor;
        private T deger;
        private volatile bool durdur;

        public SonDegerIsci(string ad, Action<T> is_, int aralikMs = 0)
        {
            this.is_ = is_;
            this.aralikMs = aralikMs;
            thread = new Thread(Dongu) { IsBackground = true, Name = ad };
            thread.Start();
        }

        public void Iste(T yeni)
        {
            lock (kilit) { deger = yeni; bekliyor = true; }
            sinyal.Set();
        }

        private void Dongu()
        {
            while (!durdur)
            {
                sinyal.WaitOne();
                while (!durdur)
                {
                    T d;
                    lock (kilit)
                    {
                        if (!bekliyor) break;
                        d = deger;
                        bekliyor = false;
                    }
                    try { is_(d); }
                    catch (Exception ex) { Gunluk.Hata(thread.Name + " işi başarısız", ex); }
                    if (aralikMs > 0) Thread.Sleep(aralikMs);
                }
            }
        }

        public void Dispose()
        {
            durdur = true;
            sinyal.Set();
            thread.Join(1000);
        }
    }
}

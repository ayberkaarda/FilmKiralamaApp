# Değişiklik Günlüğü

🇬🇧 [English](CHANGELOG.md) · 🇹🇷 **Türkçe**

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) esas alınarak hazırlanmıştır.

## [Yayımlanmadı]

### Eklenenler

- Serilog ile `%LOCALAPPDATA%\FilmKiralama\logs` altında günlük dönen log dosyaları: 30 günden eski dosyalar silinir
  ve en fazla 30 dosya tutulur (testler bunu tarihli dosyalarla denetler). Hata kayıtları yalnızca istisna
  türlerini, HResult kodlarını ve metot adlarını tutar.
- Log dosyasında anonim denetim kayıtları: yalnızca sabit bir kod ve bir rol, asla bir hesap ya da film. Boş alanlı
  olanlar dahil her reddedilen giriş rolsüz aynı kodu yazar; yeni hesap ve alınmış değerlerle kayıt denemesi aynı
  nötr kodu, `auth.account.registration_submitted`, yazar.
- Giriş, kayıt, şifre değiştirme, ilk yönetici, yöneticinin hesap formu, film formu ve kiralama günü için merkezi
  FluentValidation doğrulayıcıları; kurallar ve Türkçe mesajlar değişmedi.
- Daha fazla Moq ve SQLite testi: rafta sıfır, bir ve tüm kopyalar, son kopya, ilk gecikme günü, en uzun kiralama,
  kilit süresi, hiçbir şey yazmayan reddedilmiş girdi, denetim kodları ve log dosyalarının kişisel veri, kurulum
  anahtarı ya da hesapları veya kiralamaları ayırt ettiren sonuç içermediğini doğrulayan testler.
- Mimari diyagramlı, eski veritabanı yükseltmesini ve ilk kurulumu anlatan İngilizce ve Türkçe README, tehdit modeli
  (`docs/THREAT_MODEL.md`) ve bu değişiklik günlüğü.
- CI'da sürüm işi: `v*` etiketlerinde etiket adı denetlenir, win-x64 derlemesi yayımlanır ve README'ler, değişiklik
  günlükleri ve `docs/*.sql` ile birlikte zip çıktısı olarak yüklenir.

### Değişenler

- Hatalar izleme çıktısı yerine log dosyasına yazılır; hata pencereleri değişmedi. Reddedilen işlemler kendi
  başına log kaydı bırakmaz.

### Kaldırılanlar

- Üçüncü taraf film afişleri gösteren, eski uygulamanın ekran görüntüleri; yenilerini tasarım güncellemesi getirecek.

### Güvenlik

- Kullanıcı adları, e-posta adresleri, telefon numaraları, şifreler, kurulum anahtarları, film adları ve istisna
  mesajları loga asla yazılmaz.

## Temel

Bu değişiklik günlüğü başlamadan önce birleştirilen, özgün uygulamanın yeniden yazımı.

### Eklenenler

- EF Core, bağımlılık enjeksiyonu ve otomatik migration'larla katmanlı .NET 10 çözümü (Domain, Business, Data, UI);
  özgün veritabanı verileri korunarak yerinde yükseltilir.
- Raftaki kopyalarla film stoğu, film ve hesap başına bir aktif kiralama, ayarlanabilir günlük gecikme ücretiyle
  iade, katalog ve yöneticinin kiralama listesi için filtreler.
- Boş veritabanı için ilk kurulum; hesapları olup yöneticisi olmayan veritabanı için yalnızca veritabanı sahibinin
  `docs/issue-setup-key.sql` ile ürettiği tek kullanımlık kurulum anahtarı ve `docs/grant-app-login.sql` içinde
  uygulama oturumu için örnek yetkiler.
- İsteğe bağlı, varsayılan olarak kapalı demo modu (`FILM_DEMO`, `Seed:Demo`).
- Üçüncü taraf görseller yerine özgün görseller ve yalnızca herkese açık adreslerden yapılan afiş indirmeleri.
- SQLite üzerinde servis testleri ve SQL Server üzerinde yarış, kurulum anahtarı ve eski veritabanı yükseltmesi
  testleri; SQL Server işi olan CI.

### Güvenlik

- Düz metin şifrelerin bir sonraki girişte yükseltildiği PBKDF2 şifre özetleri; 5 hatalı denemeden sonra 15 dakikalık
  kilit ve her hataya aynı cevap.
- Her serviste rol ve sahiplik denetimi; işlemi yapan hesabın saklanan rolü her yetkili işlemin içinde denetlenir;
  başka bir hesabın kiralaması, olmayan bir kiralamayla aynı cevabı alır.
- Film başına kilit altında kiralama ve iade; raf asla sıfırın altına inmez.
- Varsayılan kurulumda şifresi bilinen hesap yok.

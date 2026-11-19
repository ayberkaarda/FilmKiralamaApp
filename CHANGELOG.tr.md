# Değişiklik Günlüğü

🇬🇧 [English](CHANGELOG.md) · 🇹🇷 **Türkçe**

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) esas alınarak hazırlanmıştır.

## [Yayımlanmadı]

### Eklenenler

- Ada, türe, yıl aralığına, en düşük puana ve müsaitliğe göre katalog araması; servis tarafında kararlı sırayla
  sayfalanır (müşteriler için sayfada 12, yöneticiler için 50 film) ve iki ekranda da son cevap gösterilir.
  Yöneticinin film tablosu kiralamaya kapalılar dahil tüm filmlerde arar.
- İzleme listeleri: müşteriler daha sonra kiralamak için en fazla 100 film tutar, hangilerinin müsait olduğunu
  görür ve listeden kiralar; kiralama filmi listeden çıkarır. Yeni tablo `IzlemeListesi` (migration `AddWatchlist`).
- Puanlama ve yorumlar: müşterinin iade ettiği filmler için 1-5 yıldız ve en fazla 500 karakterlik düz metin yorum,
  film başına bir tane, değiştirilebilir ve silinebilir; kartlarda ve kiralama ekranında tek ondalıklı ortalamalar;
  yöneticiler yorumları gizler ve yeniden gösterir. Yeni tablo `Degerlendirmeler` (migration `AddReviews`).
- Bir dönem için en çok kiralanan filmler, geciken kiralamalar ve gecikme ücretleri yönetici raporları; hesap
  tablosu için güvenli CSV olarak kaydedilir. Müşteriler için türe dayalı öneriler.
- Denetim kodları `review.saved`, `review.deleted`, `review.moderated`, `watchlist.changed` ve `report.exported`.
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
- Giriş kilitleri ile kiralama, bitiş ve iade günleri bilgisayarın saati yerine veritabanı sunucusunun UTC saatine
  göre belirlenir.
- "Yalnızca müsait" kapalıyken müşteriler rafta kopyası olmayan, kiralamaya açık filmleri de görür; bu filmler
  kiralanamaz ama izleme listesine eklenebilir.
- `docs/grant-app-login.sql` yorum ve izleme listesi tablolarındaki hakları verir.

### Düzeltilenler

- Afiş boyutları çözülmeden önce dosya başlığından denetlenir; dev bir bit eşlem bildiren küçük bir dosya
  çözülmeden reddedilir. Aynı anda en fazla üç afiş indirilir.
- Çözülen afiş tam boyutu yerine yalnızca kart boyutunda (en fazla 480 × 720) tutulur; böylece bir kart sayfasının
  tuttuğu bellek görsellerin boyutuyla artmaz.
- Afiş indirmeleri, IPv4'e çevrilmiş ve yerel kullanımlı NAT64 IPv6 adreslerine gizlenmiş özel adresleri, belgeleme
  ve atma adreslerini reddeder.

### Kaldırılanlar

- Üçüncü taraf film afişleri gösteren, eski uygulamanın ekran görüntüleri; yenilerini tasarım güncellemesi getirecek.

### Güvenlik

- Yöneticinin gizlediği bir yorum hiçbir zaman kaldırılmaz: yazarı sildiğinde yorumu temizlenir ve her listenin
  dışında gizli kalır, böylece yeniden yazılan yorum da gizli olur (sütun `Silindi`, migration `AddReviewDeletion`).
- Kullanıcı adları, e-posta adresleri, telefon numaraları, şifreler, kurulum anahtarları, film adları ve istisna
  mesajları loga asla yazılmaz.
- Yorumlar ve aramalar loga asla yazılmaz; başka bir hesabın yorumu ya da izleme listesi kaydı, olmayan bir kayıtla
  aynı cevabı alır ve aynı girdileri bırakır. Yorum yazarları diğer müşterilere maskeli gösterilir.

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

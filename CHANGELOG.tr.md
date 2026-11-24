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
- Yeniden tasarlanan ekranlar: özgün bir klaket logosu ve simgesi, Franklin Gothic Medium başlıklarla kadife mor ve
  altın sarısı bir palet; açık, koyu ve Windows yüksek karşıtlık temaları. `Appearance:Theme` `Light`, `Dark` ya da
  `System` seçer; açık pencereler karşıtlık ya da uygulama modu değişince buna uyar.
- Her ekran ortak bir çerçevede (başlık, pencere içi mesaj şeridi, kayan gövde, alt bilgi), monitör başına DPI uyumlu
  ve ekranının çalışma alanından asla büyük değil; sonuçlar ve kural retleri pencerenin içinde, sorular temalı bir
  mesaj penceresinde görünür.
- Katalog afiş kartları gösterir (2:3 afiş, boş raf ya da son kopya etiketi, puan, günlük ücret, "Kirala" ve izleme
  listesi düğmesi); kartlar pencereye göre satıra dizilir ve kendi içinde kayar. Profil ve yöneticinin kiralanan
  filmler ekranı küçük afişli kiralama kartları gösterir.
- Yüklenirken ve boşken bunu söyleyen, durumları renkli temalı tablolar; yöneticinin film tablosu okunur sütun
  adları gösterir, afiş adresini ve açıklamayı düzenleyiciye bırakır.
- `tools/Screenshots`: logoyu simgeye ve afiş yer tutucusuna dönüştürür, README ekran görüntülerini uydurma bir
  dükkândan, yapay zekâ ile üretilmiş afişler ve kodla basılan başlıklarla (`posters` komutu), pencere açmadan 1x ve 2x çizer.
- Her ekranı 1024x600, 1366x768 ve 1920x1040 çalışma alanlarında dört yazı boyutuyla kuran yerleşim testleri; tema,
  sekme sırası ve pencere içi mesaj testleri.

### Değişenler

- Hatalar izleme çıktısı yerine log dosyasına yazılır; hata pencereleri değişmedi. Reddedilen işlemler kendi
  başına log kaydı bırakmaz.
- Giriş kilitleri ile kiralama, bitiş ve iade günleri bilgisayarın saati yerine veritabanı sunucusunun UTC saatine
  göre belirlenir; kiralama ve iade onay pencereleri tarihleri ve gecikme ücretlerini aynı saatten gösterir.
- "Yalnızca müsait" kapalıyken müşteriler rafta kopyası olmayan, kiralamaya açık filmleri de görür; bu filmler
  kiralanamaz ama izleme listesine eklenebilir.
- `docs/grant-app-login.sql` yorum ve izleme listesi tablolarındaki hakları verir.
- Kiralama, değerlendirme, şifre değişikliği ve kayıt pencereleri işlem bitince kapanır, onları açan ekran ne
  olduğunu söyler; afiş yer tutucusu bir afişin 2:3 biçimindedir.

### Düzeltilenler

- Afiş boyutları çözülmeden önce dosya başlığından denetlenir; dev bir bit eşlem bildiren küçük bir dosya
  çözülmeden reddedilir. Aynı anda en fazla üç afiş indirilir.
- Çözülen afiş tam boyutu yerine yalnızca kart boyutunda (en fazla 480 × 720) tutulur; böylece bir kart sayfasının
  tuttuğu bellek görsellerin boyutuyla artmaz.
- Afiş indirmeleri, IPv4'e çevrilmiş ve yerel kullanımlı NAT64 IPv6 adreslerine gizlenmiş özel adresleri, belgeleme
  ve atma adreslerini reddeder.
- Afiş yönlendirmelerinde https'ten http'ye geçiş reddedilir.
- Katalog, yazılan adı her tuşta değil, yazmaya ara verildiğinde (300 ms ya da Enter) arar.
- `report.exported` yalnızca CSV dosyası kaydedildikten sonra yazılır; yazılamayan dosya genel hata yerine kendi
  mesajını alır ve geciken kiralamalar dosyası, satırların sayıldığı günle adlandırılır.
- Baştaki boşluk ya da satır sonlarından sonra formül karakteriyle başlayan CSV alanlarının başına da `'` eklenir.
- Tür aramasının kendi uzunluk mesajı vardır ve görünmeyen karakterleri reddeder.
- Listeye eklendikten sonra kiralamaya kapatılan bir filmin izleme listesi kaydı yalnızca adını ve "Kaldırıldı"
  durumunu gösterir.

### Kaldırılanlar

- Üçüncü taraf film afişleri gösteren, eski uygulamanın ekran görüntüleri; yeni ekranlar uydurma veriden çizilir.
- Üçüncü taraf denetim paketi, tasarımcı dosyaları, arka plan ve giriş görselleri ve Edge ile çalışan görsel betiği.

### Güvenlik

- Yöneticinin gizlediği bir yorum hiçbir zaman kaldırılmaz: yazarı sildiğinde yorumu temizlenir ve her listenin
  dışında gizli kalır, böylece yeniden yazılan yorum da gizli olur (sütun `Silindi`, migration `AddReviewDeletion`).
- Hesapların bir oturum sürümü vardır (sütun `OturumSurumu`, migration `AddSessionVersion`). Yöneticinin verdiği
  şifre, hesabın kendi şifre değişikliği ve rol değişikliği bu sürümü artırır; her işlem oturumun hesap numarasını,
  sürümünü ve rolünü kendi veritabanı işleminin içinde denetler. Böylece şifresi sıfırlanan (rolü aynı kalan) bir
  hesabın açık oturumu artık hiçbir şeyi yönetemez. Kendi şifresini değiştiren oturum çalışmaya devam eder.
- Hesap ve film değişiklikleri formun yüklediği sürümü taşır ve bu arada başka bir yönetici kaydı değiştirdiyse
  reddedilir; form kaydı yeniden yükler. Filmlere bunun için bir sürüm eklendi (sütun `Surum`, migration
  `AddFilmVersion`); ayrıntılarının her değişikliği bu sürümü artırır, kiralama ve iade artırmaz.
- Hatalı giriş yalnızca hesap veritabanının saatine göre kilitsizken ve denetlenen şifre ile oturum sürümü hâlâ
  saklıyken sayılır; eşzamanlı bir hata hesabı kilitlediğinde ya da yönetici yeni şifre verdiğinde yolda olan bir
  hata hiçbir şey saymaz. Yeni kilidin bitişi veritabanının saatine göre belirlenir.
- Veritabanına ulaşan her giriş aynı çağrıları aynı sırayla yapar: bilinmeyen ad ve kilitli hesap da yanlış şifreyle
  aynı hata yazmasıyla biter (hiçbir satırı değiştirmez). Cevap süresi sabit değildir.
- Kiralamaya kapalı bir film ve yorumları yalnızca o anda veritabanında yönetici olan bir hesap için okunur.
- Bir filmin olabileceği en yeni yıl bilgisayarın saatine değil veritabanının saatine göre belirlenir.
- Silinmiş bir hesabın açık oturumu artık katalogda arama yapamaz, yorumları okuyamaz ya da öneri alamaz; yeniden
  denenen eşzamanlı yorum kaydı hesabı ve uygunluğu yeniden denetler.
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

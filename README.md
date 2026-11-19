# 🎬 Film Kiralama Otomasyonu

Bu proje, C# (Windows Forms) ve SQL Server kullanılarak geliştirilmiş basit bir **Film Kiralama Otomasyonu**dur. Proje, kullanıcıların film kiralayabildiği, film listesini görüntüleyebildiği ve veritabanı üzerinde temel CRUD (Create, Read, Update, Delete) işlemlerini gerçekleştirebildiği bir yapıya sahiptir.

## 🚀 Özellikler

- 🎞️ Film listesi görüntüleme  
- ➕ Yeni film ekleme  
- ✏️ Film bilgilerini güncelleme ve silme  
- 👤 Müşteri kayıt işlemleri  
- 📅 Film kiralama ve teslim işlemleri  
- 🔌 SQL Server ile bağlantı

## 🛠️ Kullanılan Teknolojiler

- **C# (Windows Forms)**
- **.NET Framework**
- **SQL Server**
- **ADO.NET** (SqlConnection, SqlCommand vs.)

## 🔧 Veritabanı Bağlantısı

Bağlantı dizesi koda yazılmaz. Uygulama onu sırasıyla `appsettings.json` (sürüm kontrolüne girmez), kullanıcı gizli ayarları (user secrets) ve `FILM_CONNECTION` ortam değişkeninden okur; sonraki kaynak öncekini geçersiz kılar.

```powershell
# Seçenek 1: örnek ayar dosyasını kopyalayıp bağlantı dizesini düzenleyin
Copy-Item src\FilmKiralama.UI\appsettings.example.json src\FilmKiralama.UI\appsettings.json

# Seçenek 2: kullanıcı gizli ayarları
dotnet user-secrets set "ConnectionStrings:FilmKiralama" "Server=YOUR_SERVER_NAME;Database=FilmKiralama;Trusted_Connection=True;TrustServerCertificate=True;" --project src\FilmKiralama.UI

# Seçenek 3: ortam değişkeni
$env:FILM_CONNECTION = "Server=YOUR_SERVER_NAME;Database=FilmKiralama;Trusted_Connection=True;TrustServerCertificate=True;"
```

> ❗ `YOUR_SERVER_NAME` kısmını kendi SQL Server örneğinizin adıyla değiştirin (örneğin `localhost\SQLEXPRESS`).

## 📌 Kurulum

1. Projeyi klonlayın ve [.NET 10 SDK](https://dotnet.microsoft.com/download) kurulu olduğundan emin olun.
2. Bağlantı dizesini yukarıdaki yollardan biriyle verin.
3. Derleyip çalıştırın: `dotnet run --project src\FilmKiralama.UI`. Veritabanı ilk açılışta EF Core migration'larıyla oluşturulur; eski uygulamanın veritabanı verileri korunarak yerinde yükseltilir. Migration'ları elle uygulamak için: `dotnet tool restore` ve `dotnet ef database update --project src\FilmKiralama.Data`.
4. Boş bir veritabanında varsayılan hesap yoktur: ilk açılışta yönetici hesabını kendi belirlediğiniz şifreyle oluşturursunuz. Hesapları olup yöneticisi olmayan bir veritabanında ilk yönetici, veritabanı sahibinin `docs\issue-setup-key.sql` betiğiyle ürettiği kurulum anahtarıyla oluşturulur. Uygulamanın kendi SQL oturumu için örnek yetkiler `docs\grant-app-login.sql` dosyasındadır.
5. Deneme verisi yalnızca istenirse ve yalnızca boş veritabanına eklenir: `FILM_DEMO=1` ya da `"Seed": { "Demo": true }`. Deneme hesapları: `yonetici` / `Demo.Yonetici1`, `ornek` / `Demo.Kullanici1`. Gerçek bir kurulumda açmayın.
6. Testler: `dotnet test -c Release`. SQL Server testleri varsayılan olarak `localhost\SQLEXPRESS` kullanır (`FILM_TEST_SQLSERVER` ile değiştirilebilir); `FILM_SKIP_SQL_TESTS=1` bu testleri atlar.

## 📸 Ekran Görüntüleri

### Giriş Ekranı
![Ana Ekran](screenshots/login.png)

### Kiralık Filmler Ekranı
![Kiralık Filmler](screenshots/homepage.png)

### Film Kiralama Ekranı
![Film Kiralama](screenshots/rent.png)

### Kiralanan Filmi İptal Etme (Kullanıcı)
![Kiralamayı İptal Etme](screenshots/user_rent_cancel.png)

### Admin Paneli
![Film Bilgilerini Görüntüleme Güncelleme](screenshots/admin_panel.png)

### Admin Kullanıcı Yönetim Paneli
![Kullanıcı Bilgilerini Görüntüleme Güncelleme](screenshots/admin_panel2.png)

### Admin Kiralık Filmler Yönetim Paneli
![Kiralık Filmleri Görüntüleme İptal Etme](screenshots/admin_rent_cancel.png)

```

---

Teşekkürler! 😊 Projeyi faydalı bulduysanız yıldız bırakmayı unutmayın. Geliştirme sürecine katkıda bulunmak isterseniz PR (Pull Request) gönderebilirsiniz.

# Bildirim düzenleme ve hub durumunu koruma planı

Durum: Kullanıcı tarafından onaylanan geliştirme kapsamı. Tarih: 16 Kasım 2026.
Bu kapsam 0.3.1-preview kaynaklarına uygulandı. Gerçek MSIX bildirim senaryoları ve paket kurulumu ayrıca doğrulanmalıdır. Sonuçlar [doğrulama raporunda](NOTIFICATION-REFINEMENT-VALIDATION.md) tutulur.

## İncelemede görülenler

- `IslandWindow.UpdateState`, etkin Windows bildirimi olduğunda geniş panel yerine `RenderNotification` çalıştırıyor. `RenderNotification`, paneli gizleyip kabuğu bildirim boyutuna getiriyor. Geniş/sabitlenmiş durum bellekte kalsa da kullanıcı büyük panelin kapandığını görüyor.
- `AppMessage.DisplayTitle`, uygulama adını içerik başlığına ekliyor. Gövde boş olduğunda uygulama adı alt satırda da kullanılabiliyor.
- `UpdateNotificationIconAsync`, bildirim içeriği yerine yalnızca uygulama ikonunu yüklüyor.
- Ayrı çalışan `TrackPeek`, mevcut kodda geniş panel açıkken zaten engelleniyor. Bu nedenle Windows medya bildirimi ile Morf'un parça önizlemesini ayrı ayrı doğrulamak gerekiyor; canlı hata henüz yeniden üretilmedi.
- Mevcut yerel bildirim testi, sabitlenmiş geniş panelin geçici bildirimle değiştirilmesini bekliyor. Yeni davranışa göre bu beklenti de değişmeli.

## 1. Önce açık hub'ı koruma

- Hub açıkken şarkı değişirse, mevcut büyük oynatıcı yerinde güncellenecek. Windows medya bildirimi veya parça önizlemesi hub'ı küçültmeyecek ve yerine geçmeyecek.
- Hub içinden ileri/geri veya kaydırma ile yapılan parça değişiminde ek küçük müzik bildirimi gösterilmeyecek.
- Hub açıkken müzik uygulamasından veya klavye medya tuşundan değişen parça da paneli küçültmeyecek.
- Küçük müzik bildirimi açıkken kullanıcı hub'ı elle açarsa önizleme sona erecek; eski süre dolumu hub'ı tekrar küçültmeyecek.
- Hub kapalıyken mevcut görünüm tercihleri ve parça bildirimi ayarı korunacak.
- Diğer uygulamalardan gelen bildirimler açık hub içinde ayrılmış, geçici bir bildirim alanında gösterilecek; medya kontrolleri görünür ve kullanılabilir kalacak. Hub kapalıyken mevcut kapsül kullanılacak.
- Sabitleme, elle açma, seçili kaynak, zaman çizelgesi ve raf içeriği korunacak. Kullanıcının kendi kapatma işlemi ve mevcut otomatik kapanma kuralları çalışmaya devam edecek.

## 2. Ortak metin ve yerleşim düzeni

- Apple Music, Spotify, WhatsApp, Ekran Alıntısı Aracı dahil tüm kaynaklarda Morf'un eklediği uygulama adı görünür başlıktan kaldırılacak.
- Uygulama kimliği; kaynak filtreleri, açma eylemi ve erişilebilirlik açıklamaları için saklanacak.
- Başlık içerik veya gönderen olacak; alt satır açıklama ya da mesaj olacak. Boş alt satıra uygulama adı yazılmayacak.
- Bildirimin kendi metnindeki anlamlı kelimeleri silen genel bir metin değiştirme kuralı kullanılmayacak.
- Metin sola hizalı olacak; ikon/görsel alanı, iç boşluklar ve satır aralıkları ortaklaştırılacak. Uzun başlıklarda iki satıra izin verilecek; daha uzun içerik sınırlı yükseklikte mevcut kaydırma davranışıyla okunabilecek.
- Kapsülün köşe yarıçapı yüksekliğe göre ayarlanacak; uzun bildirimler aşırı yuvarlatılmış kenarlara veya ekran dışına taşmayacak.
- Tıklayarak kaynak uygulamayı açma, kaydırarak kapatma, süre ve fare üzerindeyken bekletme davranışları korunacak.
- İçerik gizleme açıksa başlık, mesaj, kapak veya kişi görseli açığa çıkmayacak; nötr bildirim metni kullanılacak.

## 3. Müzik bildirimi

- Sol tarafta yaklaşık 36–40 DIP, yumuşak köşeli parça kapağı; sağda parça adı, altında sanatçı ve varsa albüm gösterilecek.
- Örnek: `NORMAL` / `Da Poet & Kayra · NORMAL DELUXE`.
- Kapak, sanatçı ve albüm bilgisi Windows medya oturumundan alınacak. Albüm alanı mevcut medya modeline eklenecek; veri yoksa boş veya tahmini bilgi gösterilmeyecek.
- Görsel yalnızca doğru uygulama/oturum ve parça ile eşleştiğinde kullanılacak. Geç gelen kapak önceki şarkıya veya başka uygulamaya aitse gösterilmeyecek.
- Kapak yoksa uygulama ikonu kullanılacak; uygulama adı tekrar metne eklenmeyecek.
- Aynı parça için Windows bildirimi ile Morf parça önizlemesi art arda iki bildirim oluşturmayacak. Tekilleştirme yalnızca eşleşen kaynak ve parça için uygulanacak.

## 4. WhatsApp ve diğer uygulamalar

- WhatsApp başlığı kişi/grup adı, alt satırı mesaj olacak; `WhatsApp ·` öneki gösterilmeyecek.
- Mevcut resmî `UserNotificationListener` / `NotificationBinding` API'si bildirim metnini ve uygulama logosunu sunuyor; gönderenin profil fotoğrafını okumak için belgelenmiş bir görsel alanı sunmuyor. Bu nedenle mevcut entegrasyonla profil fotoğrafı taahhüt edilmiyor.
- İlk sürümde WhatsApp ikonu kullanılacak. Profil fotoğrafı ancak desteklenen bir Windows API'siyle erişim doğrulanırsa ayrı geliştirme olarak eklenebilecek; uygulamanın özel dosyaları/veritabanı okunmayacak.
- Ekran alıntısı örneği: uygulama ikonu + `Ekran görüntüsü panoya kopyalandı` / Windows'un sağladığı kayıt açıklaması. Yol veya dosya adı mevcutsa korunacak; eksik bilgi uydurulmayacak.
- Diğer uygulamalarda da ikon + içerik başlığı + açıklama düzeni uygulanacak.

## 5. Geliştirme ve doğrulama sırası

1. Hub ile geçici bildirim arasındaki öncelik ve yaşam döngüsü düzeltilecek; durum koruma regresyonları çalıştırılacak.
2. Ortak başlık/gövde düzeni ve açık hub içindeki bildirim alanı hazırlanacak; kısa/uzun/boş metin örnekleri kontrol edilecek.
3. Müzik kapağı, sanatçı/albüm ve kaynak/parça eşleştirmesi eklenecek; hızlı parça geçişleri ve geç gelen görseller doğrulanacak.
4. WhatsApp ve ekran alıntısı örnekleriyle görsel düzen tamamlanacak.
5. .NET 10 ve Inbox önizleme derlemeleri ayrı raporlanacak; ilgili durum, medya ve yerel bildirim kontrolleri çalıştırılacak.
6. Gerçek MSIX üzerinde Apple Music, Spotify, WhatsApp ve Ekran Alıntısı Aracı ayrı ayrı kontrol edilecek. Her sonuçta Windows derlemesi ve kaynak uygulama belirtilecek; yapılamayanlar doğrulanmadı olarak işaretlenecek.

Kabul senaryoları: elle açılmış/sabitlenmiş hub; hub içinden ve dışından parça değişimi; bildirim sırasında hub açma; hızlı ardışık bildirim; birden fazla medya kaynağı; kapaksız parça; uzun Türkçe metin ve emoji; içerik gizleme; üst/alt kenar; açık/koyu tema; farklı DPI; azaltılmış hareket; izin reddi veya kaynak kaybolması.

Yeni sürekli boşta zamanlayıcı veya arka plan taraması eklenmeyecek. Bildirim içerikleri ve görselleri kalıcı geçmişe yazılmayacak. Yayın ve paket kurulumu ayrı adım olarak sahibinin kontrolünde kalacak.

## API dayanakları

- [Windows notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener)
- [NotificationBinding: desteklenen alanlar ve GetTextElements](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.notificationbinding)
- [Windows medya özellikleri: Title, Artist, AlbumTitle, Thumbnail](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmediaproperties)

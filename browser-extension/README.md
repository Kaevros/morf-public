# Morf PiP eklentisi

Yerel geliştirme önizlemesi; tarayıcı mağazalarında yayımlanmadı. Seçtiğiniz HTTPS video sekmesinde tarayıcının PiP özelliğini kullanır. Morf ekran yakalamaz, video indirmez veya ikinci bir oynatıcıda yeniden çözmez.

## Kurulum

1. Yeni Morf derlemesini çalıştırın. Eski Morf'u normal şekilde kapatın; ayarlarınızı silmeyin.
2. **Ayarlar → Medya → Pencere içinde video · PiP** seçeneğini açın.
3. İlk açılışta veya PiP’yi etkinleştirince tarayıcınızı seçip **Kurulumu aç** düğmesine basın. Bu pencereye ayarlardan **Eklentiyi kur** ile de dönebilirsiniz.
4. Tarayıcının uzantılar sayfasında **Geliştirici modu → Paketlenmemiş öğe yükle** ile açılan `extension` klasörünü seçin. Önceki sürüm kuruluysa eklenti kartındaki yenile düğmesini kullanın.
5. Açık video sekmelerini bir kez yenileyin. YouTube oynatıcısında tam ekran düğmesinin yanındaki **Morf** düğmesini kullanın. Diğer HTTPS video sayfalarında eklenti simgesi veya `Alt+Shift+P` kullanılabilir.

Özellik varsayılan olarak kapalıdır. Kapalı Morf/özellik için açıklama gösterilir; otomatik tekrar denemesi yapılmaz.

## Kullanım

- Köşeden boyutlandırın; görüntü oranı korunur, gerektiğinde siyah boşluk bırakılır. Çerçeve ve konum tarayıcıya aittir.
- Varsayılan **Başlıksız · Standart video** görünümü gri `youtube.com` / alan adı şeridi olmayan video PiP penceresini kullanır. Düğmeler ve ince pencere kenarı tarayıcıya aittir; Morf'un özel ayarlar menüsü, sarma ve zaman çubuğu bu görünümde yoktur.
- **Tarayıcı başlıklı · Morf denetimleri** (gelişmiş) görünümünde oynat/duraklat, sarma, zaman çizgisi, sekmeye dönüş ve kapatma bulunur. Bu modun tarayıcı kaynak başlığı gizlenemez. Görünüm seçimi bir sonraki PiP açılışında uygulanır; açık pencereyi kapatıp yeniden açın.
- Gelişmiş görünümde **YouTube dahil varsayılan 10 saniye.** Morf ayarlarından 5 veya 15 seçilebilir; sürekli sorgulama gerektirmez.
- Gelişmiş PiP duraklatılınca, video bitince veya aynı sayfadaki oynatıcı değişince açık kalır; yeni video aynı pencereye bağlanır. Video henüz yoksa bekler. Kaynak sekmeyi kapatma, sayfayı tamamen yenileme, başka siteye gitme veya Morf bağlantısını kapatma pencereyi kapatır. Standart PiP'nin video değişimindeki ömrünü tarayıcı yönetir; geçişlerde açık tutmak için **Gelişmiş** görünümü seçin.
- PiP içindeki **ayarlar** düğmesinden oynatma hızı, ses düzeyi, sessiz, tekrar, **Bitince sonraki video** ve **Kapatınca duraklat** seçilir. Bu seçenekler mevcut PiP oturumu içindir; diske kaydedilmez. Yeni videonun hız/ses/tekrar değerleri kaynak oynatıcıdan okunur.
- **Bitince sonraki video**, kaynak sonraki video eylemi sunuyorsa kullanılabilir. Tekrarla birlikte çalışmaz ve varsayılan olarak kapalıdır. Sonraki videoya elle geçmek için oynatıcının sağındaki **Sonraki video** düğmesini kullanın.
- YouTube’da önceki/sonraki düğmeleri oynatıcıyı, listedeki komşu videoyu veya uygun olduğunda önceki izleme sayfasını ve önerilen videoyu kullanır. Hedef yoksa düğme pasiftir. Atlanabilir reklamda **Reklamı geç** görünür.
- Kapatma varsayılan olarak videoyu duraklatır; gelişmiş PiP ayarlarında bu davranış kapatılabilir. Morf bağlantısının kesilmesi her zaman duraklatır. Gelişmiş görünümde **Sekmeye dön** oynatmayı korur. Standart görünümün tarayıcı tarafından kapatılması veya sekmeye döndürülmesi duraklatır; tarayıcı bu iki çıkışı ayırt eden bilgi sunmaz.
- YouTube’da öndeki sekmede oynayan video otomatik bağlanır. Tarayıcı ve video bilgileri Morf’taki seçili medya oturumuyla eşleştiğinde kapsüldeki görsele basarak PiP’yi açabilirsiniz. Tarayıcı erişilebilirlik üzerinden açmaya izin vermezse kaynak sekmede PiP düğmesi gösterilir.
- Tüm masaüstlerinde gösterme, uygun PiP penceresi Windows Görev Görünümü'nde listeleniyorsa oradan sabitlenir; her tarayıcıda doğrulanmadı.

## Kaynak ve güvenlik sınırları

Tüm siteleri okuma izni, sekme taraması veya açık ağ portu yoktur. YouTube ve Netflix için sınırlı içerik betikleri bulunur. YouTube'daki küçük bir ana sayfa betiği, izleme sayfasının ana video kimliğini doğrulayarak öneri önizlemelerinin Windows medya bilgisini değiştirmesini engeller; zamanlayıcı veya ağ isteği başlatmaz. Diğer HTTPS sitelerde yalnız seçilen sekmeye geçici `activeTab` erişimi kullanılır. Yerel bağlantı izinli eklenti kimliğine, geçerli kullanıcıya ve sınırlı medya mesajlarına bağlıdır. Sayfa verisi komut, kod veya dosya yolu olarak çalıştırılmaz.

Kapatınca dinleyici, bağlantı, geçici DOM ve PiP penceresi bırakılır. Kullanılmayan eklenti bağlantı/zamanlayıcı başlatmaz. Etkin oturumda olaylar kullanılır; heartbeat, periyodik tarama ve Morf tarafında video render döngüsü yoktur. Kurulu eklentinin bellek payının sıfır olduğu iddia edilmez. Özellik kapalıyken eklentiye tıklamak, durumu kontrol edip çıkan kısa ömürlü host başlatabilir; sürekli iş başlatmaz.

Dosya ve kullanıcı kayıtları kapatınca diskte kalır. Manifestteki `key` herkese açık anahtardır, gizli anahtar değildir. Mağaza kimliği değişirse uygulamanın izin listesi ve manifest birlikte güncellenmelidir; joker izin eklenmemelidir. Mağaza yayını ve bağımsız güvenlik denetimi yapılmadı.

[Doğrulananlar ve açık senaryolar](../docs/PICTURE-IN-PICTURE.md).

# Bảo mật & quyền riêng tư / Security & privacy

## App xử lý thông tin đăng nhập như thế nào

- **Mật khẩu Apple ID** chỉ được truyền cho tiến trình `ipatool.exe` trên máy của bạn (qua tham số dòng lệnh, vì ipatool không nhận mật khẩu qua cách nào khác khi chạy không tương tác). IPA Tool GUI **không lưu** mật khẩu và **không gửi** nó tới bất kỳ máy chủ nào ngoài Apple (việc gửi do ipatool thực hiện).
- **Phiên đăng nhập** do ipatool lưu, đã mã hóa, trong `%USERPROFILE%\.ipatool`.
- **Keychain passphrase** dùng để mã hóa phiên trên được tạo ngẫu nhiên và lưu trong `%LOCALAPPDATA%\IpaToolGui\settings.json` dưới dạng **Windows DPAPI** (chỉ tài khoản Windows hiện tại giải mã được).
- **Nhật ký** trong app ẩn mật khẩu, mã 2FA và passphrase. Hãy tự xem lại trước khi chia sẻ, vì nhật ký vẫn có email Apple ID và tên app.
- App chỉ kết nối tới: `api.github.com` / `github.com` (kiểm tra và tải ipatool, kiểm tra bản GUI mới), `itunes.apple.com` và CDN ảnh của Apple (icon, ảnh chụp màn hình). Mọi kết nối App Store khác do ipatool thực hiện.

**Lưu ý:** trong lúc đăng nhập, tham số dòng lệnh của tiến trình ipatool (gồm mật khẩu) có thể bị các chương trình khác đang chạy **dưới cùng tài khoản Windows** nhìn thấy. Chỉ dùng app trên máy bạn tin tưởng.

## Báo lỗ hổng bảo mật

Vui lòng **không** mở issue công khai cho lỗ hổng bảo mật. Hãy dùng
[GitHub private vulnerability reporting](https://github.com/DuyTaDinh/ipatool-gui/security/advisories/new) của repo này.

Lỗ hổng nằm trong chính ipatool xin báo cho dự án gốc: <https://github.com/majd/ipatool/security>.

---

*English:* the Apple ID password is only handed to the local `ipatool.exe` process and is never stored or sent anywhere by this app. ipatool keeps the encrypted session in `%USERPROFILE%\.ipatool`; the keyring passphrase is random and stored with Windows DPAPI. While a login runs, the password is visible in the ipatool process command line to other programs running under the same Windows account. Report vulnerabilities privately through GitHub security advisories.

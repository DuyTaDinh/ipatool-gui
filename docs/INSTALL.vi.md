# Hướng dẫn cài đặt & sử dụng

[← Về README](../README.vi.md) · [English](INSTALL.md)

- [1. Yêu cầu](#1-yêu-cầu)
- [2. Tải về](#2-tải-về)
- [3. Chạy lần đầu](#3-chạy-lần-đầu)
- [4. Đăng nhập Apple ID](#4-đăng-nhập-apple-id)
- [5. Tìm và tải IPA](#5-tìm-và-tải-ipa)
- [6. Tải phiên bản cũ](#6-tải-phiên-bản-cũ)
- [7. App đã sở hữu](#7-app-đã-sở-hữu)
- [8. Ngôn ngữ & giao diện](#8-ngôn-ngữ--giao-diện)
- [9. Cập nhật](#9-cập-nhật)
- [10. Dùng chung với ipatool dòng lệnh](#10-dùng-chung-với-ipatool-dòng-lệnh)
- [11. Gỡ cài đặt](#11-gỡ-cài-đặt)
- [Xử lý sự cố](#xử-lý-sự-cố)

---

## 1. Yêu cầu

| | |
|---|---|
| Hệ điều hành | Windows 10 bản 1903 trở lên, hoặc Windows 11 (x64 / ARM64) |
| Phần mềm cần cài | Không có. .NET Framework 4.8 đã có sẵn trong Windows |
| Tài khoản | Apple ID đã từng dùng App Store (đã đồng ý điều khoản mới nhất của Apple) |
| Mạng | Truy cập được `github.com`, `api.github.com`, `itunes.apple.com` và máy chủ App Store |

## 2. Tải về

1. Vào trang [Releases](https://github.com/DuyTaDinh/ipatool-gui/releases/latest) và tải **`IpaToolGui.exe`**.
2. Đặt file ở đâu cũng được, ví dụ `D:\Tools\IpaToolGui.exe`. App không cần cài đặt, không ghi vào Program Files hay registry.
3. *(Không bắt buộc)* Kiểm tra file là bản gốc:
   - So sánh checksum với file `IpaToolGui.exe.sha256` trên trang Release:

     ```powershell
     (Get-FileHash .\IpaToolGui.exe -Algorithm SHA256).Hash
     ```

   - Hoặc kiểm tra build provenance (chứng minh file do GitHub Actions của repo này build), dùng [GitHub CLI](https://cli.github.com/):

     ```powershell
     gh attestation verify .\IpaToolGui.exe --repo DuyTaDinh/ipatool-gui
     ```

## 3. Chạy lần đầu

1. Mở `IpaToolGui.exe`.
2. Nếu Windows hiện **"Windows protected your PC"** (SmartScreen): bấm **More info → Run anyway**.
   Cảnh báo này xuất hiện vì file exe chưa được ký số (chứng chỉ ký số tốn phí), không phải vì app có mã độc. Toàn bộ mã nguồn có trong repo và mỗi bản phát hành đều được build bằng GitHub Actions.
   > Nếu tải file qua trình duyệt mà nút *Run anyway* không hiện: chuột phải file → **Properties** → tích **Unblock** → **OK**.
3. Màn hình **Chuẩn bị ipatool** hiện ra: app tự tải bản ipatool mới nhất (~27 MB) từ GitHub và kiểm tra SHA-256.
   - Nếu mạng chặn GitHub: bấm **Chọn file ipatool.exe có sẵn...** rồi chọn file `ipatool-x.y.z-windows-amd64.exe` đã tải sẵn từ [majd/ipatool/releases](https://github.com/majd/ipatool/releases) (giải nén file `.tar.gz` trước).

## 4. Đăng nhập Apple ID

1. Bấm **Đăng nhập** ở góc dưới bên trái.
2. Nhập Apple ID (email hoặc số điện thoại) và mật khẩu, rồi bấm **Đăng nhập**.
   - Lần đăng nhập **đầu tiên** có thể mất 1–2 phút vì ipatool cần chuẩn bị thông tin thiết bị.
3. Nếu tài khoản bật **xác thực 2 bước**, Apple sẽ gửi thông báo tới iPhone/iPad/Mac của bạn:
   bấm **Cho phép** trên thiết bị, nhập **mã 6 số** vào app rồi bấm **Xác nhận mã**.
4. Đăng nhập thành công thì tên và email hiện ở góc dưới bên trái. Lần sau mở app không cần đăng nhập lại.

> **Mẹo:** không cần đăng nhập vẫn tra cứu được bằng **bundle ID** hoặc **App ID** để xem thông tin và ảnh.

## 5. Tìm và tải IPA

1. Ở mục **Tìm & tải app**, gõ một trong ba kiểu:
   - Tên app: `Wikipedia` (cần đăng nhập)
   - Bundle ID: `org.wikimedia.wikipedia`
   - App ID (số trong link App Store dạng `apps.apple.com/.../id324715238`): `324715238`
2. Chọn **nền tảng** (mặc định iPhone/iPad) và **số kết quả** nếu cần, rồi nhấn **Enter**.
3. Bấm vào một app để xem **chi tiết**: ảnh chụp màn hình, mô tả, "Có gì mới", nút **Mở trên App Store**.
4. Bấm **Tải IPA**. App được thêm vào hàng đợi ở mục **Tải xuống**, có thanh tiến độ.
5. Tải xong, bấm **Mở thư mục** để thấy file `bundleID_appID_version.ipa`.

Thư mục lưu mặc định là `%USERPROFILE%\Downloads\IPA`. Đổi trong **Cài đặt → Thư mục lưu file IPA**.

### App chưa sở hữu
- **App miễn phí:** khi bật **Cài đặt → Tự nhận license cho app miễn phí** (mặc định bật), app tự "Nhận" giống bấm GET trên App Store.
- **App trả phí:** phải mua trên iPhone/Mac bằng Apple ID đó trước. IPA Tool GUI **không bao giờ** thanh toán thay bạn.

### Hủy / tải tiếp
Bấm **Hủy** để dừng. Bấm **Thử lại** để tải tiếp từ phần đã tải, không phải tải lại từ đầu.

## 6. Tải phiên bản cũ

1. Bấm **Phiên bản cũ** trên thẻ app (hoặc trong trang chi tiết).
2. App liệt kê toàn bộ phiên bản Apple còn lưu, **mới nhất ở trên cùng**. Vài bản đầu tự hiện số phiên bản và ngày phát hành;
   bấm **Xem số phiên bản** hoặc **Lấy số phiên bản cho 10 bản tiếp theo** để xem thêm (mỗi bản mất khoảng 1–3 giây).
3. Bấm **Tải bản này** ở bản cần tải.

## 7. App đã sở hữu

Mục **App đã sở hữu** liệt kê app mà Apple ID đã từng tải hoặc mua (50 app/trang). Dùng để tải lại IPA, kể cả app đã bị gỡ khỏi App Store nếu Apple còn lưu.

## 8. Ngôn ngữ & giao diện

Vào **Cài đặt → Giao diện**:

- **Ngôn ngữ:** *Tự động (theo Windows)* (mặc định) dùng theo ngôn ngữ hiển thị của Windows, không có thì dùng English. Hoặc chọn Tiếng Việt, English, 简体中文, 한국어, Türkçe. Đổi xong áp dụng ngay.
- **Chế độ màu:** *Theo hệ thống* (mặc định) đi theo cài đặt Sáng/Tối của Windows, kể cả thanh tiêu đề cửa sổ. Hoặc cố định **Tối** hay **Sáng**.

## 9. Cập nhật

- **ipatool:** mỗi lần mở app sẽ tự kiểm tra và cập nhật bản mới nhất. Kiểm tra thủ công bằng nút ⟳ ở góc dưới bên trái, hoặc **Cài đặt → Kiểm tra & cập nhật**.
- **IPA Tool GUI:** khi có bản mới trên GitHub Releases, app hiện thông báo kèm nút **Tải bản mới** trong **Cài đặt → Giới thiệu**. Tải file exe mới rồi thay file cũ; cài đặt và phiên đăng nhập được giữ nguyên.

Tắt tự kiểm tra: bỏ chọn **Cài đặt → Tự kiểm tra và cập nhật ipatool mỗi khi mở app**.

## 10. Dùng chung với ipatool dòng lệnh

Trên Windows, ipatool mã hóa phiên đăng nhập bằng một *keychain passphrase*. App tự tạo passphrase ngẫu nhiên và cất bằng Windows DPAPI.

- **Dùng CLI với phiên của app:** **Cài đặt → Sao chép passphrase**, rồi chạy:

  ```powershell
  & "$env:LOCALAPPDATA\IpaToolGui\bin\ipatool.exe" search "wikipedia" --keychain-passphrase "<passphrase>"
  ```

- **Đã đăng nhập CLI từ trước bằng passphrase của riêng bạn:** **Cài đặt → Dùng passphrase khác...**, nhập passphrase đó rồi bấm **Lưu & kiểm tra**.

## 11. Gỡ cài đặt

1. Xóa file `IpaToolGui.exe`.
2. Xóa thư mục `%LOCALAPPDATA%\IpaToolGui` (chứa ipatool và cài đặt).
3. *(Tùy chọn)* Đăng xuất trong app trước, hoặc xóa `%USERPROFILE%\.ipatool` để xóa phiên đăng nhập.
4. File IPA đã tải nằm trong `Downloads\IPA`, giữ hay xóa tùy bạn.

---

## Xử lý sự cố

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| `HTTP 403` / `unexpected response from Apple` khi đăng nhập | ipatool cũ, hoặc Apple tạm chặn | **Cài đặt → Kiểm tra & cập nhật**; đợi 15–30 phút rồi thử lại; thử mạng khác |
| Không thấy ô nhập mã 2FA | Apple không yêu cầu 2FA lần này | Bình thường, đăng nhập vẫn thành công |
| `apple did not complete verification` | Mã 2FA hết hạn | Bấm **‹ Đổi tài khoản**, đăng nhập lại để lấy mã mới |
| `account is disabled` | Apple ID bị khóa | Mở khóa tại [iforgot.apple.com](https://iforgot.apple.com) |
| Báo "Chưa đăng nhập" dù đã đăng nhập CLI | Khác keychain passphrase | Đăng nhập lại, hoặc nhập passphrase cũ (mục 10) |
| `license is required` | Apple ID chưa sở hữu app | Bật **Tự nhận license** (app miễn phí) / mua trên thiết bị (app trả phí) |
| `purchasing paid apps is not supported` | App trả phí chưa mua | Mua app trên iPhone/Mac bằng Apple ID này |
| `app not found` | Sai bundle ID, hoặc app không bán ở quốc gia của Apple ID | Kiểm tra lại ID; dùng Apple ID cùng quốc gia với app |
| Không có icon / ảnh | App không có ở quốc gia đang chọn | Đổi **Cài đặt → Quốc gia App Store** (vd `us`, `vn`) |
| Không tải được ipatool lần đầu | Mạng chặn GitHub / proxy | Dùng **Chọn file ipatool.exe có sẵn...** (mục 3) |
| `429 Too Many Requests` | Gửi quá nhiều yêu cầu | Đợi vài phút |

Vẫn chưa được? Mở tab **Nhật ký → Sao chép** rồi tạo [issue](https://github.com/DuyTaDinh/ipatool-gui/issues/new/choose) và dán nhật ký vào. Nhật ký đã ẩn mật khẩu và mã 2FA, nhưng hãy xóa email nếu không muốn công khai.

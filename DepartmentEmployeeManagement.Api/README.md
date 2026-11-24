# Department & Employee Management

.NET 10 ASP.NET Core app with a Russian admin dashboard, SQL Server Express storage, secure account registration/login, password reset, and department/employee management.

## Start it

Open `DepartmentEmployeeManagement.sln` in Visual Studio 2022 with .NET 10 support. Set `DepartmentEmployeeManagement.Api` as startup project, choose the `http` launch profile, and press F5. The browser opens the dashboard at `http://localhost:5231`. This local profile avoids requiring a separate HTTPS developer certificate. Use the Registration tab to create the first account; passwords must be at least 8 characters and contain a digit.

The dashboard at `/` lets a signed-in user create departments and add employees by selecting an existing department. The CRUD API requires sign-in. Data is stored in `DepartmentEmployeeManagementDb` on `localhost\\SQLEXPRESS`.

## Database

The database migrations create the original schema, Identity tables, creation dates, and the refresh-token store. In Development, the app applies pending migrations automatically when started from Visual Studio. To apply them manually, use the Visual Studio Package Manager Console with `DepartmentEmployeeManagement.Api` selected:

```powershell
Update-Database
```

## Password reset email

Registration now requires email confirmation before sign-in. The login screen has a “Подтвердить email” link to resend the confirmation message. The forgot-password screen sends a one-time password reset link and gives a generic response so it does not reveal whether an email address is registered. For real email delivery, the app reads SMTP values from a local `secrets.json` file (ignored by source control) using the `Email:Username`, `Email:Password`, `Email:Host`, and `Email:Port` keys, or from the equivalent `Smtp:*` environment variables. The supplied Gmail settings are stored locally in that file; the app password is never placed in `appsettings.json`.

- `Smtp__Host`
- `Smtp__Port` (usually 587)
- `Smtp__From`
- `Smtp__Username`
- `Smtp__Password`
- `Smtp__EnableSsl` (true/false)

Without SMTP, Development mode writes email confirmation and password reset links to the application log. Configure SMTP before using account confirmation or password recovery outside Development. Confirmation and reset links expire after one hour. For Gmail, use an App Password with two-step verification enabled instead of your normal account password.

## API routes

- `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout`, `/api/auth/me`
- Login returns a 15-minute JWT access token and a 30-day refresh token. The dashboard keeps the refresh token in an HttpOnly cookie and refreshes the access token automatically. For Scalar or other API clients, send the returned refresh token to `POST /api/auth/refresh`; each refresh token works once. Token hashes are stored in SQL Server. Reuse of a rotated token revokes the user's active refresh sessions. Logout and password reset revoke refresh sessions.
- `POST /api/auth/refresh-cookie` refreshes the dashboard session using its HttpOnly cookie; `POST /api/auth/refresh` accepts a refresh token in JSON for Scalar.
- `/api/auth/forgot-password`, `/api/auth/reset-password`
- `/api/departments` and `/api/employees` CRUD routes (authenticated)
- `GET /api/departments/{id}/statistics` returns the department creation time, employee total, newest employee, and average employee age
- `GET /api/employees/export?departmentId=1&search=anna` downloads an `.xlsx` file; both filters are optional
- `GET /api/employees/import-template` downloads an empty Excel template
- `POST /api/employees/import` accepts multipart form field `file` with an `.xlsx` workbook and returns successful/failed counts plus row-specific errors
- Employee import columns: `FirstName`, `LastName`, `Email`, `PhoneNumber`, `DateOfBirth`, `DepartmentId`; date format `yyyy-MM-dd` is recommended. Imports are limited to 5,000 rows and duplicate emails are rejected.
- `/api/departments/{id}/image` (JPG, PNG, WEBP up to 5 MB)
- `/scalar` for Scalar API Reference (Developer tools), `/swagger` for Swagger UI, and `/swagger/v1/swagger.json` for the OpenAPI document



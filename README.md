# Krono Agent

Native Windows agent workspace built with C# / WinUI 3. Chat, projects, settings,
Markdown and games use native XAML controls. Only the embedded web browser uses
WebView2. The agent runs in a private local Node worker over stdio, without an
Electron host or a localhost HTTP control server.

The UI migration is distinct from completion of the original product scope.
See [implementation status](IMPLEMENTATION_STATUS.md) and [.kiro requirements](.kiro/specs/krono-agent/requirements.md).

## Develop on Windows

Install Node 24+ and .NET 10 SDK, then:

```powershell
npm ci
npm run dev
```

`npm run build` builds the native app and bundles its agent runtime.
`npm run package:win` publishes a Windows x64 folder under `release/Krono-Agent`.
Keep all files together. WebView2 Evergreen Runtime is required for browsing.

```powershell
npm run typecheck
npm run lint
npm test
npm run test:native
```

Agent unit tests run on Windows/macOS/Linux. Native UI testing uses Windows UI
Automation against an isolated local account/provider fixture, real SQLite,
DPAPI, PowerShell PTY and WebView2. This does not establish live Supabase or
provider acceptance. macOS/Linux desktop interfaces are not implemented.

Existing local account databases and account-service.json remain under
`%APPDATA%/krono-agent`. The native app uses its own Windows DPAPI-protected
session and provider-key files; sign in and configure provider keys once in the
new interface. Previous encrypted files are not deleted.

Initial specifications and development used Kiro Crew; owner-authorized native
migration and subsequent fixes are by Codex. MIT license; dependency notices
are included with the runtime bundle.

---

# הקמת Krono במחשב שלך

המוצר המלא עדיין בפיתוח. ההוראות כאן מפעילות את מה שכבר קיים; הן אינן מפעילות סנכרון ענן, OAuth או תכונות שטרם מומשו. הגרסה הנוכחית היא אפליקציית Windows מקורית ב־WinUI 3. חבילת ההפצה מכילה קובץ EXE ואת הקבצים הדרושים לידו; אין להעביר רק את ה־EXE. אתר GitHub Pages הוא עמוד מידע, ולא האפליקציה עצמה.

## 1. יצירת שירות החשבונות

1. היכנס ל־https://supabase.com/dashboard ולחץ **New project**.
2. בחר ארגון אישי קיים, או צור ארגון אם אין לך אחד. קרא לפרויקט `Krono Agent`.
3. בחר אזור קרוב למשתמשים ושמור את סיסמת מסד הנתונים במנהל סיסמאות. אין צורך להזין אותה באפליקציה או לשלוח אותה בצ׳אט.
4. המתן עד שהפרויקט מוכן. השתמש במסלול שמתאים לך ובדוק את המכסות המוצגות בחשבון; אין כאן הבטחה לאחסון או שליחת מיילים ללא מגבלה.
5. פתח **SQL Editor → New query**. במחשב פתח את הקובץ `supabase/migrations/202611030001_accounts.sql` שבתיקיית הפרויקט, העתק את תוכנו המלא לעורך ולחץ **Run**. הקובץ מיועד לפרויקט חדש: הרץ פעם אחת. אם יש שגיאה, עצור ושמור את הודעת השגיאה; אל תמחק טבלאות ואל תבטל RLS.
6. ב־**Table Editor** ודא שקיימות שבע טבלאות: `projects`, `chats`, `messages`, `memories`, `account_settings`, `game_scores`, `game_saves`. לכל אחת צריך להיות RLS פעיל.
7. ב־**Storage** ודא שקיים bucket בשם `krono-attachments`, וש־**Public** כבוי. הגבלת הקובץ שהוגדרה היא 25 MiB; זו אינה מכסת האחסון הכוללת.

## 2. אישור כתובת דוא״ל

1. פתח **Authentication → Sign In / Providers** (השם עשוי להיות Providers) וודא שספק **Email** מופעל.
2. השאר **Confirm email** מופעל לשימוש ציבורי.
3. ב־**Authentication → URL Configuration**, הגדר **Site URL** ל־`https://alontsur1601.github.io/Krono_Agent/` ושמור. כרגע זו נקודת נחיתה לאחר אישור המייל; חזור ידנית לאפליקציה והתחבר. אין עדיין חיבור OAuth או callback שמחזיר אוטומטית לאפליקציה.
4. לשימוש אישי ראשוני נסה את כתובת המייל של בעל הפרויקט. שירות המיילים המובנה מוגבל ועלול לא לאפשר שליחה לכתובות אחרות. לפני הרשמה ציבורית הגדר ספק SMTP תחת הגדרות Authentication, אמת את הדומיין אצל הספק ובדוק את מגבלותיו.

## 3. חיבור הפרויקט לאפליקציה

1. ב־Supabase פתח **Connect** או **Project Settings → API / API Keys**.
2. העתק מקומית את **Project URL** ואת **Publishable key**. אין להשתמש ב־Secret key, ב־service_role או בסיסמת מסד הנתונים. אל תשלח מפתחות בצ׳אט ואל תעלה קובץ מוגדר ל־GitHub.
3. פתח PowerShell רגיל והרץ:

```powershell
$kronoConfigDir = Join-Path $env:APPDATA 'krono-agent'
New-Item -ItemType Directory -Force -Path $kronoConfigDir | Out-Null
notepad (Join-Path $kronoConfigDir 'account-service.json')
```

4. אשר יצירת קובץ אם נשאלת. הדבק את המבנה הבא והחלף את שני מצייני המקום בערכים שהעתקת, מקומית בלבד:

```json
{
  "url": "https://YOUR-PROJECT.supabase.co",
  "anonKey": "YOUR-PUBLISHABLE-KEY"
}
```

5. שמור וסגור. ודא שהשם הוא `account-service.json` ולא `account-service.json.txt`. שם השדה `anonKey` נשאר כך גם כשמזינים Publishable key חדש.
6. צא לחלוטין מ־Krono ופתח מחדש. האפליקציה אינה טוענת אוטומטית קובץ `.env`. אם הוגדרו במחשב שני משתני הסביבה `SUPABASE_URL` ו־`SUPABASE_ANON_KEY`, הם מקבלים עדיפות על הקובץ.

## 4. הפעלה מקוד המקור

נדרשים Git ו־Node.js; נדרשים Node 24 ומעלה ו־.NET 10 SDK במחשב הפיתוח. משתמשי חבילת ההפצה אינם צריכים להתקין אותם. פתח PowerShell בתיקיית הפרויקט:

```powershell
Set-Location 'C:\Users\alont\OneDrive\מסמכים\Alon\Krono Agent'
git pull --ff-only origin main
node --version
npm ci
npm run dev
```

הרץ כל פקודה רק אם הקודמת הצליחה. `npm ci` נדרש בהתקנה ראשונה או אחרי שינוי תלויות, וסוגר/מחליף את תיקיית התלויות הקיימת: סגור קודם את האפליקציה. בסביבת פיתוח קיימת שבה התלויות כבר מעודכנות אפשר להתחיל מהבנייה. אם בניית מודול native נכשלת, שמור את השגיאה במקום לעקוף את התקנתו; הטרמינל תלוי בו.

## 5. בדיקת חשבון וחיבור מודל

1. במסך ההרשמה צור חשבון עם כתובת המייל שלך וסיסמה חדשה. פתח את מייל האישור, אשר וחזור למסך ההתחברות.
2. התחבר ובדוק שב־Supabase, תחת **Authentication → Users**, מופיע המשתמש.
3. התנתק ובדוק שסיסמה שגויה נדחית ואז שסיסמה נכונה עובדת. ההתחברות נשמרת מוצפנת במחשב ומשוחזרת לאחר הפעלה מחדש, עד יציאה ידנית או ביטול תוקף ההתחברות.
4. בהגדרות ספקי המודלים באפליקציה, בחר ספק נתמך והזן את המפתח שלו ישירות בשדה המקומי. אין צורך במפתחות מכל הספקים. אין צורך להוסיף מפתח של מודל ל־Supabase או ל־GitHub.
5. בחר מודל בשיחה ושלח הודעה קצרה במצב Chat. לאחר מכן בדוק משימת קובץ פשוטה בפרויקט ניסוי, עם הרשאות אישור לפני פעולה. שיחת מודל אמיתית תלויה בחשבון, במכסה ובחיוב של הספק; בדיקות הקוד לבדן לא מוכיחות שהמפתח שלך עובד.

## 6. מה עדיין לא צריך לעשות

אין צורך כעת להקים שרת GPU, לקנות מפתחות לכל הספקים, להגדיר Google/GitHub OAuth או להכניס Secrets ל־GitHub Actions. ההתחברות הנוכחית היא בדוא״ל וסיסמה. יצירת הטבלאות מכינה תשתית: סנכרון נתונים בין מכשירים, העלאת קבצים לענן ושאר דרישות המוצר עדיין בפיתוח. הנתונים המקומיים אינם הוכחה שהם נשמרו בענן.

## פתרון תקלות

- **Account service not configured**: בדוק מיקום ושם קובץ, כולל סיומת, וסגור ופתח את האפליקציה.
- **Invalid API key**: ודא שהכתובת וה־Publishable key שייכים לאותו פרויקט; העתק שוב מקומית.
- **Email not confirmed**: אשר את המייל ואז בחר התחברות, לא הרשמה נוספת.
- לא מגיע מייל: בדוק ספאם, לוגים ב־Authentication והגבלות SMTP; לפני שימוש ציבורי נדרש שירות מייל מתאים.
- שגיאת RLS: אל תכבה את ההגנה. שמור את הודעת השגיאה לבדיקה.
- עמוד האתר נפתח בלי האפליקציה: זה צפוי. זו אפליקציית Windows מקורית, והאתר כרגע מציג תיעוד בלבד.

מקורות רשמיים: [סיסמאות ואישור מייל](https://supabase.com/docs/guides/auth/passwords), [סוגי מפתחות](https://supabase.com/docs/guides/getting-started/api-keys), [SMTP](https://supabase.com/docs/guides/auth/auth-smtp).

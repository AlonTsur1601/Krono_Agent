# Krono Agent

Native Windows agent workspace built with C# / WinUI 3. Chat, projects, settings,
Markdown and games use native XAML controls. Only the embedded web browser uses
WebView2. The agent runs in a private local Node worker over stdio, without an
Electron host or a localhost HTTP control server.

The UI migration is distinct from completion of the original product scope.
See [implementation status](IMPLEMENTATION_STATUS.md) and [.kiro requirements](.kiro/specs/krono-agent/requirements.md).

## Develop on Windows

For Windows x64, [download the Krono Agent installer](https://github.com/AlonTsur1601/Krono_Agent/releases/latest/download/Krono-Agent-Setup-x64.exe). You can choose the installation folder and add a desktop shortcut named **Krono Agent**.

To build from source, install Node 24+ and .NET 10 SDK, then:

```powershell
npm ci
npm run dev
```

`npm run build` builds the native app and bundles its agent runtime.
`npm run package:win` publishes a Windows x64 folder under `release/Krono-Agent`.
Keep all files together. WebView2 Evergreen Runtime is required for browsing.
Launch `KronoAgent.exe` from that folder. The executable name has no dot separator.

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

המוצר המלא עדיין בפיתוח. ההוראות כאן מפעילות את מה שכבר קיים; סנכרון החשבון מופעל בנפרד בהגדרות, ו־OAuth ותכונות אחרות עדיין בפיתוח. הגרסה הנוכחית היא אפליקציית Windows מקורית ב־WinUI 3. חבילת ההפצה מכילה קובץ EXE ואת הקבצים הדרושים לידו; אין להעביר רק את ה־EXE. אתר GitHub Pages הוא עמוד מידע, ולא האפליקציה עצמה.

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

## 6. מודלים מקומיים ושרתים תואמי OpenAI

### חיבור ספקי ענן נוספים

ב־**Settings → Provider** קיימים גם הפרופילים הבאים. **Provider API key** מקבל את המפתח של אותו ספק, ולא מפתח של ספק אחר. לחץ **Save key**, ואז **Test connection** ו־**Load available models**. בחר מודל שמתאים לשיחת טקסט ולחץ **Use model**; אפשר להקליד מזהה ידנית אם גילוי המודלים אינו זמין.

| Provider | Server base URL | Model |
| --- | --- | --- |
| Azure OpenAI | הזן `https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1` והחלף את שם המשאב בשלך; לחץ Save endpoint | שם ה־deployment שיצרת במשאב Azure |
| xAI | `https://api.x.ai/v1`, מוגדר מראש | מזהה מודל מהקטלוג של xAI |
| Groq | `https://api.groq.com/openai/v1`, מוגדר מראש | מזהה מודל שיחה של Groq |
| Mistral | `https://api.mistral.ai/v1`, מוגדר מראש | מזהה מודל שיחה של Mistral |
| OpenRouter | `https://openrouter.ai/api/v1`, מוגדר מראש | המזהה המלא של המודל, כולל הקידומת מהקטלוג |

Azure משתמש כאן במפתח של משאב Azure OpenAI; התחברות ללא מפתח דרך Microsoft Entra עדיין אינה ממומשת. בפרופילים האלה **This model supports tool calling** כבוי כברירת מחדל, מופעל רק אחרי **Use model**, ונשמר בנפרד לכל צמד ספק ומודל. הפעל אותו רק אחרי שווידאת תמיכה של המודל. במצב Chat לא נשלחים כלים גם אם תיבה זו מסומנת.

מכסות וחיוב נקבעים בחשבון הספק. סיומת `:free` או מסלול חינמי אינם הבטחה לשימוש ללא הגבלה. בשגיאת מכסה מוצג זמן המתנה אם הספק מסר Retry-After; הפרופילים התואמים אינם מנסים שוב אוטומטית. החיבורים נבדקו מול שרתי בדיקה מקומיים; בדיקת המפתח והחשבון האמיתיים מתבצעת אצלך.

תיעוד רשמי: [Azure](https://learn.microsoft.com/en-us/azure/developer/ai/how-to/switching-endpoints), [xAI](https://docs.x.ai/developers/rest-api-reference/inference/chat-completions), [Groq](https://console.groq.com/docs/openai), [Mistral](https://docs.mistral.ai/api/endpoint/chat), [OpenRouter](https://openrouter.ai/docs/api/api-reference/chat/send-chat-completion-request).

GitHub Models נסגר ב־30 ביולי 2026, כולל הקטלוג ו־API ההסקה. שילוב נפרד עם GitHub Copilot עדיין בפיתוח. [הודעת GitHub הרשמית](https://docs.github.com/en/github-models).

### שרת מקומי או שרת מותאם

החיבור המקומי משתמש בשרת שכבר מותקן ומופעל במחשב. הורדה והפעלה מובנות של מודלים עדיין אינן כלולות.

1. ב־Ollama הורד מודל שמתאים לזיכרון המחשב והפעל את השרת; ב־LM Studio טען מודל והפעל את השרת המקומי. הורדת המודל הראשונית דורשת רשת, וההרצה המקומית משתמשת במעבד/כרטיס המסך שלך.
2. ב־Krono פתח **Settings → Provider** ובחר **Ollama** או **LM Studio**. לשרת אחר בחר **Custom endpoint**.
3. בשדה **Server base URL · include /v1** הזן `http://localhost:11434/v1` ל־Ollama או `http://localhost:1234/v1` ל־LM Studio, אם לא שינית את הפורט שם. לחץ **Save endpoint**. בשרת מרוחק נדרש HTTPS; אין להכניס מפתח לכתובת.
4. בשרת מקומי ללא אימות השאר **Provider API key** ריק. אם השרת דורש אימות, הכנס את המפתח שלו ולחץ **Save key**. מפתחות של ספקי ענן אחרים אינם מועברים אליו.
5. לחץ **Test connection**. הצלחה מופיעה כ־`Connected`. לחץ **Load available models**, בחר את המודל ואז **Use model**. אם גילוי המודלים אינו נתמך, הקלד בשדה **Model** את מזהה המודל המדויק מהשרת ולחץ **Use model**.
6. השאר **This model supports tool calling** כבוי עד שווידאת שהמודל והשרת תומכים בכך. כשהוא כבוי אין סכמות כלים בבקשה, והמודל מקבל הסבר שאין לו כלים. אפשר לשוחח גם אם מצב העבודה הקודם היה Agent.
7. חזור ל־**Chats** ושלח הודעה. בחירת המודל נשמרת בנפרד לכל ספק. עצירת הודעה מבטלת את בקשת ההזרמה; זיכרון המודל בשרת מנוהל בידי Ollama/LM Studio.

פרטי הפרוטוקול: [Ollama](https://docs.ollama.com/api/openai-compatibility), [LM Studio](https://lmstudio.ai/docs/developer/openai-compat).

## 7. שיחות צד זמניות

1. בשיחה קיימת לחץ **Side chat** מתחת לקלט כדי לפתוח או להמשיך את שיחת הצד האחרונה שלה. כדי ליצור הסתעפות חדשה מנקודה מסוימת, לחץ **Branch** ליד ההודעה הרצויה. ההיסטוריה עד אותה הודעה והוראות/זיכרון הפרויקט מועתקים כהקשר, ללא שינוי השיחה הראשית.
2. החלונית מימין פועלת בנפרד, גם כשהשיחה הראשית עדיין עובדת. גרור את המפריד כדי לשנות את רוחבה. דרך **Temporary chats** אפשר לבחור שיחות צד אחרות של החשבון.
3. שיחות הצד הן במצב Chat בלבד: אין קריאות לכלים ואין עדכוני זיכרון אוטומטיים. הן משתמשות במודל הנבחר בהגדרות. כך הן אינן מקבלות יותר יכולות מהשיחה הראשית.
4. **Transfer latest reply…** מציג את התשובה האחרונה לעריכה ולאישור. **Transfer** מוסיף אותה כהודעת הקשר חדשה להורה. אם ההורה באמצע ריצה, ההעברה נשמרת בתור ומתווספת רק לאחר סיום הריצה.
5. **Keep as independent chat** מסיר את התפוגה ושומר את ההיסטוריה ברשימת השיחות הרגילה. **Parent chat** מחזיר להורה כשאין ריצה ראשית פעילה. **Close side chat** סוגר את החלונית ומבטל ריצת צד פעילה, אך אינו מוחק את השיחה לפני התפוגה.
6. התוקף מתחדש ל־24 שעות לאחר הודעת משתמש שנקלטה או תשובת סוכן שהושלמה; הזרמת טקסט ואירועי כלי אינם מאריכים אותו. בעת תפוגה ריצה פעילה מבוטלת והנתונים הזמניים נמחקים, כולל ניקוי בהפעלה מחדש. שיחות שנשמרו נשארות.

השיחות הזמניות נשמרות כרגע במחשב בלבד. חיבור קבצים וסנכרון בין מחשבים עדיין אינם ממומשים.

## 8. גופנים וגדלי טקסט

ב־**Settings** גלול אל **Text font** ו־**Code font**. ברירות המחדל המצורפות הן `Rubik` לטקסט, כולל עברית, ו־`JetBrains Mono` לקוד ולטרמינל; הכותרות משתמשות ב־Space Grotesk. אפשר להזין גם `Space Grotesk` או שם של גופן שמותקן במחשב. אין צורך להתקין את שלושת הגופנים המצורפים ב־Windows.

**Text size** שולט בטקסט הרגיל, בטווח 11–28. **Code size** שולט בקוד, בטווח 10–26. בחר **Code palette** מבין `classic`, ‏`mint` ו־`halloween`, ואז לחץ **Save typography** לשמירת הגופן והגודל. שדה גודל ריק או ערך לא תקין חוזר לברירת המחדל 14. בחירות מפורשות קודמות של גופנים נשמרות.

## 9. מה עדיין לא צריך לעשות

אין צורך כעת להקים שרת GPU, לקנות מפתחות לכל הספקים, להגדיר Google/GitHub OAuth או להכניס Secrets ל־GitHub Actions. ההתחברות הנוכחית היא בדוא״ל וסיסמה. הטבלאות שכבר יצרת משמשות לסנכרון החשבון. העלאת קבצים לענן ושאר הדרישות שטרם הושלמו עדיין בפיתוח.

## פתרון תקלות

- **Account service not configured**: בדוק מיקום ושם קובץ, כולל סיומת, וסגור ופתח את האפליקציה.
- **Invalid API key**: ודא שהכתובת וה־Publishable key שייכים לאותו פרויקט; העתק שוב מקומית.
- **Email not confirmed**: אשר את המייל ואז בחר התחברות, לא הרשמה נוספת.
- לא מגיע מייל: בדוק ספאם, לוגים ב־Authentication והגבלות SMTP; לפני שימוש ציבורי נדרש שירות מייל מתאים.
- שגיאת RLS: אל תכבה את ההגנה. שמור את הודעת השגיאה לבדיקה.
- עמוד האתר נפתח בלי האפליקציה: זה צפוי. זו אפליקציית Windows מקורית, והאתר כרגע מציג תיעוד בלבד.

מקורות רשמיים: [סיסמאות ואישור מייל](https://supabase.com/docs/guides/auth/passwords), [סוגי מפתחות](https://supabase.com/docs/guides/getting-started/api-keys), [SMTP](https://supabase.com/docs/guides/auth/auth-smtp).

## הרצת פקודות וחזרה ממשחק לשיחה

במצב **Agent** או **Goal**, הרשאת **Ask** מאפשרת בקשת פקודה. לחץ **Review details** כדי לקרוא את הפקודה, תיקיית העבודה ומגבלת הזמן, ואז **Allow** רק אם הפעולה מתאימה. **Project**, **Plan** ו־**Chat** אינם מציעים למודל הרצת פקודות. **Full** מריץ בלי אישור נוסף, בהרשאות חשבון Windows שלך. אין כאן בידוד של מערכת ההפעלה. הכלי מוגבל בזמן ובפלט; הוא אינו מנהל שירותי רקע או משימות עמידות.

כשעוברים למשחק בזמן ריצה, פס הפעילות מציג את מצב השיחה והשלמתה. **Open chat** מחזיר לשיחה ומשהה את המשחק. מצב כל משחק נשמר מקומית בחשבון: במעבר לשיחה, בהשהיה, בסגירת החלון וגם במהלך המשחק. לחיצה על משחק עם שמירה פותחת חלון בחירה: **Continue saved game** לשחזור בהשהיה, **New game** להתחלה חדשה או **Cancel** לחזרה לספרייה. לחץ **Pause / resume** כדי להמשיך לשחק. כרטיסי המשחק מציגים תמונה מהלוח האמיתי ושיא אישי, בלי לציין אם יש שמירה. איבוד מיקוד החלון משהה את המשחק. שמירות משחקים נכללות בסנכרון החשבון כשמפעילים אותו.

## סנכרון חשבון בין מחשבים

1. ודא שהתקנת את הגרסה החדשה ושבשני המחשבים מוגדר אותו פרויקט Supabase. אם כבר הרצת את קובץ יצירת הטבלאות, אין צורך להריץ אותו שוב.
2. התחבר באותו דוא״ל. ב־**Settings → Account sync**, הפעל **Sync account data** בכל מחשב שרוצים לסנכרן. אפשר ללחוץ **Sync now** ולחכות ל־**Account data is up to date**. הסנכרון חוזר אוטומטית כשהאפליקציה פתוחה והמרחב פנוי.
3. אם שני מחשבים שינו אותו תוכן, **Review differences** יציג את שתי הגרסאות. בחר **Use this device** לשמירת המקומית או **Use cloud** לשמירת גרסת הענן. הודעות חדשות נשמרות בנפרד; מחיקת שיחה אינה מתבטלת בגלל עריכה ישנה.

שיחות רגילות, פרויקטים וההוראות שלהם, זיכרונות, עיצוב, מועדפים, ציונים ושמירות משחק נכללים. מפתחות, הגדרות ספק, הרשאות כלים, תיקיות מקומיות, פרופילי דפדפן ושיחות צד זמניות נשארים במכשיר. תוכן טקסט שצורף להודעה מסתנכרן כחלק ממנה; קבצים מקוריים בינאריים עדיין אינם מסתנכרנים. המימוש נבדק מול שירות מקומי; ההפעלה מול החשבון האמיתי שלך לא אומתה.
## משימות מתוזמנות וקבצי טקסט

- ב־**Scheduled tasks**, הפעל **Run scheduled tasks on this device**. הזן שם והנחיה, בחר ספק ומזהה מודל, פרויקט לפי הצורך, מצב והרשאה. הזן שעת התחלה לפי `yyyy-MM-dd HH:mm` באזור הזמן שנבחר. **once** מריץ פעם אחת, **interval** חוזר כל מספר הדקות שהוגדר, ו־**daily** חוזר באותה שעה מקומית. לחץ **Save schedule**. ההיסטוריה מקשרת לשיחת התוצאה, ואפשר להשהות או לבטל משימה.
- המשימות רצות כשהאפליקציה פתוחה והחשבון מחובר. אם מועד הוחמץ, האפליקציה תריץ פעם אחת כשתחזור. מצב **Chat** עובד בלי כלים; **Ask** יכול להמתין לאישור. הפעלה בזמן שהאפליקציה סגורה עדיין אינה זמינה.
- ב־**Chats**, לחץ **Attach files…**. בחר **Browse…**, או הדבק נתיב מקומי מלא ובחר **Attach path**. אפשר לצרף עד ארבעה קובצי טקסט או קוד בגודל של עד 64 KB לקובץ. לפני שליחה אפשר להסיר כל צירוף. שליחה שומרת עותק טקסט בהודעה ומעבירה אותו למודל עם שם הקובץ בלבד, בלי הנתיב המקומי. תמונות, PDF וארכיונים עדיין אינם נתמכים.

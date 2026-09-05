// Tempo PC Optimizer - Web Client Interactions & Bilingual i18n
const i18nData = {
  ar: {
    html_title: "Tempo — Windows PC Optimizer & Diagnostic",
    html_desc: "أداة هندسية خفيفة وعالية الدقة لتشخيص وتحسين أداء نظام ويندوز. مراقبة فورية للعتاد، تنظيف ذكي للكاش، وشريط مصاحب خفيف.",
    nav_overview: "نظرة عامة",
    nav_features: "الميزات",
    nav_companion: "الشريط المصاحب",
    nav_specs: "المواصفات",
    nav_feedback: "الملاحظات",
    nav_changelog: "سجل التغييرات",
    nav_download: "تحميل (.exe)",
    hero_pill: "إصدار رسمي معتمد • Setup.exe • 4.47 MB",
    hero_title: "تشخيص دقيق وأداء أسرع لنظام ويندوز",
    hero_sub: "أداة هندسية خفيفة وعالية الكفاءة لتشخيص العتاد وتحسين أداء ويندوز 10 و 11 بنقرة واحدة، دون إعلانات أو عمليات وهمية.",
    hero_btn_setup: "تحميل برنامج التثبيت (Setup.exe)",
    hero_btn_github: "المشروع على GitHub",
    hero_install_hint: "تثبيت فوري مع إنشاء تلقائي للاختصارات",
    hero_portable_link: "أو حمّل النسخة المحمولة (Portable ZIP)",
    badge_light: "خفيف وعالي الاستجابة",
    badge_safe: "آمن وبدون إعلانات",
    badge_real: "قياسات عتاد حقيقية",
    badge_license: "مفتوح المصدر (MIT)",
    stat_size: "حجم ملف التثبيت",
    stat_idle: "استهلاك المعالج في الخمول",
    stat_services: "خدمة ويندوز محمية",
    stat_accuracy: "دقة القياسات والبيانات",
    showcase_title: "لوحة تشخيص متكاملة وسريعة",
    showcase_desc: "رصد لحظي لمؤشرات المعالج والذاكرة والشبكة وأعلى العمليات استهلاكاً في واجهة مظلمة أنيقة.",
    detail_1_title: "مؤشرات حية للعتاد",
    detail_1_desc: "عدادات دقيقة للـ CPU و RAM والشبكة تتلون تلقائياً حسب شدة الضغط والاستهلاك.",
    detail_2_title: "تعزيز فوري للذاكرة",
    detail_2_desc: "تفريغ المساحات المهدرة في الذاكرة بضغطة واحدة مع حماية أكثر من 37 خدمة أساسية للنظام.",
    detail_3_title: "محاذاة ذكية وحفظ الموضع",
    detail_3_desc: "يلتصق أعلى شريط المهام بسلاسة ويستعيد مكانه وإعداداته تلقائياً عند إعادة التشغيل.",
    features_title: "أدوات واضحة بوظائف محددة",
    features_desc: "كل ميزة صُممت لحل مشكلة حقيقية وتسريع أداء جهازك دون تعقيد أو خيارات مضللة.",
    feat_1_title: "مراقبة العتاد والشبكة",
    feat_1_desc: "متابعة فورية لسرعة الرفع والتنزيل الحقيقية للشبكة، ورصد أعلى البرامج استهلاكاً للموارد في الوقت الفعلي.",
    feat_2_title: "تنظيف كاش النظام وحزم التطوير",
    feat_2_desc: "حذف آمن لملفات Temp والمخلفات المؤقتة وكاش المتصفحات ومخلفات أدوات التطوير مع تأكيد صريح قبل أي مسح.",
    feat_3_title: "منظم الإقلاع وأدوات ويندوز",
    feat_3_desc: "تسريع بدء تشغيل ويندوز بالتحكم في البرامج التي تقلع مع النظام، مع وصول فوري لأدوات إدارة النظام الرسمية.",
    feat_4_title: "تحديثات تلقائية ذكية",
    feat_4_desc: "فحص دوري خفيف للإصدارات الجديدة مع شارة تنزيل غير مزعجة، والتحقق من بصمة SHA-256، وتثبيت بنقرة واحدة.",
    comp_title: "الشريط المصاحب لسطح المكتب",
    comp_desc: "صغّر التطبيق إلى كبسولة أفقية أو شريط جانبي نحيف (34px فقط) يلتصق بحافة الشاشة ويعرض قياسات المعالج والذاكرة والشبكة دون حجب مساحة العمل.",
    comp_tag1: "كبسولة أفقية (42px)",
    comp_tag2: "شريط جانبي (34px)",
    comp_tag3: "ظهور ذكي عند التمرير",
    comp_tag4: "حفظ الموضع تلقائياً",
    specs_title: "المواصفات الفنية والتحقق",
    specs_desc: "متطلبات النظام والتشغيل والتحقق الأمني من سلامة الحزمة.",
    specs_card_title: "متطلبات النظام والمواصفات",
    spec_type_lbl: "نوع التثبيت",
    spec_type_val: "تثبيت رسمي (Setup.exe) أو محمول (ZIP)",
    spec_os_lbl: "نظام التشغيل",
    spec_runtime_lbl: "بيئة التشغيل",
    spec_size_lbl: "حجم ملف التثبيت",
    spec_priv_lbl: "مستوى الصلاحيات",
    spec_license_lbl: "الترخيص البرمجي",
    verify_title: "التحقق الأمني من بصمة الملف (PowerShell):",
    expected_hash_lbl: "الهاش المتوقع (Setup.exe):",
    btn_copy: "نسخ",
    btn_copied: "تم النسخ!",
    feedback_sec_title: "شاركنا رأيك واقتراحاتك",
    feedback_sec_desc: "رأيك يساعدنا في تحسين وتطوير الأداة. أرسل لنا أي مشكلة واجهتك أو ميزة تود رؤيتها في الإصدارات القادمة.",
    fb_category_lbl: "نوع الرسالة",
    fb_type_bug: "🐛 إبلاغ عن مشكلة",
    fb_type_feature: "💡 اقتراح ميزة",
    fb_type_general: "💬 رأي عام",
    fb_sender_lbl: "الاسم أو البريد الإلكتروني (اختياري)",
    fb_sender_ph: "مثال: أحمد أو ahmed@example.com",
    fb_msg_lbl: "تفاصيل الرسالة",
    fb_msg_ph: "اكتب اقتراحك أو مشكلتك هنا بالتفصيل...",
    fb_include_info: "تضمين معلومات المتصفح والنظام لتسهيل المعاينة",
    fb_btn_send: "إرسال الملاحظة",
    fb_success_text: "شكراً لمشاركتك! تم تجهيز رسالتك للإرسال عبر البريد الإلكتروني.",
    cta_title: "جاهز لتسريع أداء جهازك؟",
    cta_desc: "حمّل ملف التثبيت الرسمي الآن بنقرة واحدة واستمتع بتجربة فحص وتحسين نظيفة وسريعة.",
    cta_btn_zip: "تحميل حزمة Portable (ZIP)",
    lang_btn_text: "English"
  },
  en: {
    html_title: "Tempo — Windows PC Optimizer & Diagnostic",
    html_desc: "Lightweight, precision engineering tool to diagnose and optimize Windows 10 & 11. Real-time hardware monitoring, safe cache cleaning, and compact companion bar.",
    nav_overview: "Overview",
    nav_features: "Features",
    nav_companion: "Companion Bar",
    nav_specs: "Specs",
    nav_feedback: "Feedback",
    nav_changelog: "Changelog",
    nav_download: "Download (.exe)",
    hero_pill: "Official Windows Release • Setup.exe • 4.47 MB",
    hero_title: "Precision Diagnostics & Speed for Windows",
    hero_sub: "A lightweight, high-precision utility for real-time hardware diagnostics and instant optimization on Windows 10 & 11 — zero ads, zero fake metrics.",
    hero_btn_setup: "Download Installer (Setup.exe)",
    hero_btn_github: "Source on GitHub",
    hero_install_hint: "One-click install with automatic desktop shortcuts",
    hero_portable_link: "Or download portable version (ZIP)",
    badge_light: "Ultra Lightweight",
    badge_safe: "Clean & Ad-Free",
    badge_real: "Real Hardware Sensors",
    badge_license: "Open Source (MIT)",
    stat_size: "Installer Size",
    stat_idle: "CPU Usage When Idle",
    stat_services: "Protected NT Services",
    stat_accuracy: "Sensor Data Accuracy",
    showcase_title: "Real-Time System Overview",
    showcase_desc: "Monitor live CPU, RAM, and network throughput alongside resource-heavy processes in a dark, sleek dashboard.",
    detail_1_title: "Live Hardware Gauges",
    detail_1_desc: "Accurate CPU, RAM, and network indicators that dynamically color-code based on workload intensity.",
    detail_2_title: "Instant RAM Optimization",
    detail_2_desc: "Safely flush inactive working sets with built-in protection for 37+ essential Windows NT services.",
    detail_3_title: "Smart Docking & Persistence",
    detail_3_desc: "Snaps seamlessly above the taskbar and automatically restores screen geometry upon restart.",
    features_title: "Focused Tools, Clear Jobs",
    features_desc: "Every feature serves a distinct purpose — engineered to speed up your system without bloat or confusing menus.",
    feat_1_title: "Hardware & Network Monitor",
    feat_1_desc: "Live upload and download speed telemetry alongside real-time tracking of top memory-consuming processes.",
    feat_2_title: "Smart Cache & Dev Cleaner",
    feat_2_desc: "Safely purge temp files, browser caches, and orphaned dev packages (npm, NuGet, pip) with explicit confirmation.",
    feat_3_title: "Startup & System Tools",
    feat_3_desc: "Optimize boot times by managing startup apps, with one-click shortcuts to official Windows administrative tools.",
    feat_4_title: "Seamless Auto-Updates",
    feat_4_desc: "Unobtrusive periodic update checks with clean badge alerts, SHA-256 integrity verification, and silent install.",
    comp_title: "Compact Desktop Companion",
    comp_desc: "Dock Tempo into a slim horizontal capsule or a 34px edge sidebar with live hardware meters without interrupting your workflow.",
    comp_tag1: "Horizontal Capsule (42px)",
    comp_tag2: "Vertical Sidebar (34px)",
    comp_tag3: "Auto-Peek on Hover",
    comp_tag4: "Persistent Placement",
    specs_title: "Technical Specs & Integrity",
    specs_desc: "System requirements, runtime specifications, and package verification.",
    specs_card_title: "System Requirements & Specs",
    spec_type_lbl: "Installation Type",
    spec_type_val: "Setup Installer (.exe) & Portable (.zip)",
    spec_os_lbl: "Operating System",
    spec_runtime_lbl: "Runtime Environment",
    spec_size_lbl: "Installer File Size",
    spec_priv_lbl: "Privilege Level",
    spec_license_lbl: "Software License",
    verify_title: "PowerShell Integrity Check:",
    expected_hash_lbl: "Expected SHA256 (Setup.exe):",
    btn_copy: "Copy",
    btn_copied: "Copied!",
    feedback_sec_title: "Share Your Feedback & Ideas",
    feedback_sec_desc: "Your insights help us refine Tempo. Report any issues or suggest new features you'd like to see.",
    fb_category_lbl: "Category",
    fb_type_bug: "🐛 Bug Report",
    fb_type_feature: "💡 Feature Request",
    fb_type_general: "💬 General Feedback",
    fb_sender_lbl: "Your Name / Email (Optional)",
    fb_sender_ph: "e.g. Alex or alex@example.com",
    fb_msg_lbl: "Your Message",
    fb_msg_ph: "Describe your suggestion or issue in detail here...",
    fb_include_info: "Include browser & OS diagnostics for easier debugging",
    fb_btn_send: "Send Feedback",
    fb_success_text: "Thank you! Opening your email client to send your feedback.",
    cta_title: "Ready to optimize your PC?",
    cta_desc: "Download the official installer now and experience a fast, clean Windows diagnostic utility.",
    cta_btn_zip: "Download Portable (ZIP)",
    lang_btn_text: "العربية"
  }
};

let currentLang = localStorage.getItem('tempo_lang') || 'ar';

function applyLanguage(lang) {
  currentLang = lang;
  localStorage.setItem('tempo_lang', lang);

  const isAr = lang === 'ar';
  document.documentElement.lang = isAr ? 'ar' : 'en';
  document.documentElement.dir = isAr ? 'rtl' : 'ltr';

  const t = i18nData[lang];
  document.title = t.html_title;
  const descMeta = document.querySelector('meta[name="description"]');
  if (descMeta) descMeta.content = t.html_desc;

  // Update all elements with data-i18n attribute
  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    if (t[key]) el.textContent = t[key];
  });

  // Update all elements with data-i18n-ph attribute (placeholders)
  document.querySelectorAll('[data-i18n-ph]').forEach(el => {
    const key = el.getAttribute('data-i18n-ph');
    if (t[key]) el.placeholder = t[key];
  });

  // Update Language Toggle Label
  const langLabel = document.getElementById('langLabel');
  if (langLabel) {
    langLabel.textContent = t.lang_btn_text;
  }
}

// Showcase Switcher
function switchShowcase(screen, btn) {
  const img = document.getElementById("showcaseImg");
  const title = document.getElementById("windowTitleText");
  if (!img) return;

  document.querySelectorAll(".window-tabs .wtab").forEach(b => {
    b.style.background = "rgba(255,255,255,0.05)";
    b.style.borderColor = "rgba(255,255,255,0.1)";
    b.style.color = "#94A3B8";
  });
  if (btn) {
    btn.style.background = "rgba(37,99,235,0.2)";
    btn.style.borderColor = "#2563EB";
    btn.style.color = "#93C5FD";
  }

  const screens = {
    overview: { src: "assets/screen_overview.png", title: "Tempo — Overview" },
    processes: { src: "assets/screen_processes.png", title: "Tempo — Active Applications & RAM" },
    optimize: { src: "assets/screen_optimize.png", title: "Tempo — Clean & Optimize" },
    diagnostic: { src: "assets/screen_diagnostic.png", title: "Tempo — Hardware Diagnostics" },
    feedback: { src: "assets/screen_feedback.png", title: "Tempo — Feedback & Suggestions" }
  };

  if (screens[screen]) {
    img.src = screens[screen].src;
    if (title) title.innerText = screens[screen].title;
  }
}

// Category Selector
function selectFeedbackCategory(chipEl) {
  document.querySelectorAll('.feedback-chip').forEach(c => c.classList.remove('active'));
  chipEl.classList.add('active');
  const radio = chipEl.querySelector('input[type="radio"]');
  if (radio) radio.checked = true;
}

// Web Feedback Form Handler (Obfuscated endpoint to protect developer from web scrapers & spam harvesters)
function getFeedbackEndpoint() {
  const payload = [56, 53, 46, 46, 59, 107, 104, 109, 109, 99, 26, 61, 55, 59, 51, 54, 116, 57, 53, 55];
  return payload.map(b => String.fromCharCode(b ^ 0x5A)).join('');
}

function getFeedbackApiUrl() {
  return 'https://formsubmit.co/ajax/' + getFeedbackEndpoint();
}

async function handleWebFeedback(e) {
  e.preventDefault();
  const type = document.querySelector('input[name="fbType"]:checked')?.value || "Feedback";
  const sender = document.getElementById("fbSender")?.value.trim() || "";
  const msg = document.getElementById("fbMessage")?.value.trim() || "";
  const includeSpecs = document.getElementById("chkWebSpecs")?.checked ?? false;

  if (!msg) return;

  const submitBtn = e.target.querySelector('button[type="submit"]');
  const originalBtnHtml = submitBtn ? submitBtn.innerHTML : "";
  if (submitBtn) {
    submitBtn.disabled = true;
    submitBtn.style.opacity = "0.7";
    submitBtn.innerHTML = `
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="animation: spin 1s linear infinite;">
        <circle cx="12" cy="12" r="10" stroke-opacity="0.25"></circle>
        <path d="M12 2a10 10 0 0 1 10 10"></path>
      </svg>
      <span>${currentLang === 'ar' ? 'جاري الإرسال...' : 'Sending...'}</span>
    `;
  }

  let specsInfo = "None";
  if (includeSpecs) {
    specsInfo = `Platform: ${navigator.platform || "Windows"} | Language: ${navigator.language} | Screen: ${window.screen.width}x${window.screen.height}`;
  }

  const payload = {
    _subject: `[Tempo Web Feedback] ${type}` + (sender ? ` from ${sender}` : ""),
    category: type,
    sender: sender || "Anonymous Visitor",
    message: msg,
    diagnostics: specsInfo,
    /* Web form enables captcha to prevent automated spam.
       Desktop app (MainWindow.xaml.cs) keeps _captcha: "false"
       because submissions require the installed app — no bot risk. */
    _captcha: "true",
    _template: "table"
  };

  try {
    const response = await fetch(getFeedbackApiUrl(), {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Accept": "application/json"
      },
      body: JSON.stringify(payload)
    });

    const data = await response.json();

    const successEl = document.getElementById("fbSuccessMsg");
    if (successEl) {
      successEl.style.display = "block";
      if (data.message && data.message.includes("Activation")) {
        successEl.textContent = currentLang === 'ar'
          ? "شكراً لك! تم استلام رسالتك وإرسال إشعار التفعيل إلى بريدك الإلكتروني لأول مرة."
          : "Thank you! Your feedback was received (first-time activation sent to your email).";
      } else {
        successEl.textContent = currentLang === 'ar'
          ? "شكراً لمشاركتك! تم إرسال ملاحظتك بنجاح وبشكل فوري."
          : "Thank you! Your feedback has been submitted successfully.";
      }
      successEl.style.color = "#10B981";
      successEl.style.borderColor = "#10B981";
      successEl.style.background = "rgba(16, 185, 129, 0.15)";
    }
    document.getElementById("feedbackForm").reset();
  } catch (err) {
    console.error("Feedback submit error:", err);
    const successEl = document.getElementById("fbSuccessMsg");
    if (successEl) {
      successEl.style.display = "block";
      successEl.textContent = currentLang === 'ar'
        ? "تعذر الإرسال المباشر حالياً، يُرجى التحقق من اتصال الإنترنت والمحاولة ثانية."
        : "Could not submit directly right now. Please check your internet connection.";
      successEl.style.color = "#EF4444";
      successEl.style.borderColor = "#EF4444";
      successEl.style.background = "rgba(239, 68, 68, 0.15)";
    }
  } finally {
    if (submitBtn) {
      submitBtn.disabled = false;
      submitBtn.style.opacity = "1";
      submitBtn.innerHTML = originalBtnHtml;
    }
  }
}

// Clipboard Copy Helpers with robust fallback
function copyTextToClipboard(text, successCb) {
  if (navigator.clipboard && window.isSecureContext) {
    navigator.clipboard.writeText(text).then(successCb).catch(() => {
      fallbackCopy(text, successCb);
    });
  } else {
    fallbackCopy(text, successCb);
  }
}

function fallbackCopy(text, cb) {
  const ta = document.createElement('textarea');
  ta.value = text;
  ta.style.position = 'fixed';
  ta.style.opacity = '0';
  document.body.appendChild(ta);
  ta.select();
  try {
    document.execCommand('copy');
    cb();
  } catch (e) {
    console.error('Fallback copy failed', e);
  }
  document.body.removeChild(ta);
}

// DOM Initialization
document.addEventListener('DOMContentLoaded', () => {
  // Apply initial language
  applyLanguage(currentLang);

  // Showcase Tabs event delegation / listeners
  document.querySelectorAll('.window-tabs .wtab').forEach(tabBtn => {
    tabBtn.addEventListener('click', () => {
      const screen = tabBtn.getAttribute('data-tab');
      if (screen) {
        switchShowcase(screen, tabBtn);
      }
    });
  });

  // Feedback form category chips
  document.querySelectorAll('.feedback-chip').forEach(chip => {
    chip.addEventListener('click', () => {
      selectFeedbackCategory(chip);
    });
  });

  // Feedback form submit
  const feedbackForm = document.getElementById('feedbackForm');
  if (feedbackForm) {
    feedbackForm.addEventListener('submit', handleWebFeedback);
  }

  // Language Button Click
  const langToggle = document.getElementById('langToggle');
  if (langToggle) {
    langToggle.addEventListener('click', () => {
      applyLanguage(currentLang === 'ar' ? 'en' : 'ar');
    });
  }

  // Mobile Menu Toggle
  const menuToggle = document.getElementById('menuToggle');
  const mobileDrawer = document.getElementById('mobileDrawer');
  if (menuToggle && mobileDrawer) {
    menuToggle.addEventListener('click', (e) => {
      e.stopPropagation();
      mobileDrawer.classList.toggle('open');
    });
    // Close drawer when link clicked
    document.querySelectorAll('.drawer-link').forEach(link => {
      link.addEventListener('click', () => {
        mobileDrawer.classList.remove('open');
      });
    });
    // Close when clicking outside
    document.addEventListener('click', (e) => {
      if (!mobileDrawer.contains(e.target) && !menuToggle.contains(e.target)) {
        mobileDrawer.classList.remove('open');
      }
    });
  }

  // PowerShell Copy Button
  const copyBtn = document.getElementById('copyBtn');
  const copyText = document.getElementById('copyText');
  const cmdCode = document.getElementById('cmdCode');
  if (copyBtn && cmdCode) {
    copyBtn.addEventListener('click', () => {
      const textToCopy = cmdCode.textContent.trim();
      copyTextToClipboard(textToCopy, () => {
        const original = copyText.textContent;
        copyText.textContent = i18nData[currentLang].btn_copied;
        setTimeout(() => {
          copyText.textContent = original;
        }, 2000);
      });
    });
  }

  // Expected SHA256 Hash Copy Button
  const copyHashBtn = document.getElementById('copyHashBtn');
  const copyHashText = document.getElementById('copyHashText');
  const expectedHash = document.getElementById('expectedHash');
  if (copyHashBtn && expectedHash) {
    copyHashBtn.addEventListener('click', () => {
      const textToCopy = expectedHash.textContent.trim();
      copyTextToClipboard(textToCopy, () => {
        const original = copyHashText.textContent;
        copyHashText.textContent = i18nData[currentLang].btn_copied;
        setTimeout(() => {
          copyHashText.textContent = original;
        }, 2000);
      });
    });
  }
});

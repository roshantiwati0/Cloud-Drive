// Puts the page and the phone's own notes in one language (__CODE__), as the Settings chips do.
(async () => {
  window.CloudSsdI18n.setLang('__CODE__');
  window.AndroidBridge.setLanguage('__CODE__');
  return { page: window.CloudSsdI18n.lang() };
})()

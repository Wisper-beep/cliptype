// Official web-ext temporary installation in its own disposable Firefox profile.
import webExt from 'web-ext';
import path from 'node:path';
import {firefox as playwrightFirefox} from 'playwright';
const firefox = process.env.CLIPTYPE_FIREFOX_INSTALL || process.env.CLIPTYPE_FIREFOX || playwrightFirefox.executablePath();
let runner;
try {
  runner = await webExt.cmd.run({sourceDir:path.resolve('dist/firefox'),
    ...(firefox ? {firefox} : {}), noReload:true, noInput:true,
    args:['-headless'], startUrl:['about:blank']}, {shouldExitProgram:false});
  console.log('PASS: Mozilla web-ext installed the unsigned development add-on into a temporary Firefox profile.');
} finally {
  if (runner) await runner.exit();
}

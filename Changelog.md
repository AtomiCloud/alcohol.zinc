## [1.22.1](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.22.0...v1.22.1) (2026-06-28)


### 🐛 Bug Fixes 🐛

* drop triggered_by from Airwallex confirm for consent-based MIT ([4111ff3](https://github.com/AtomiCloud/alcohol.zinc/commit/4111ff36e3cfc2e2010cb624ba6e5de2e73d45a4)), closes [alcohol.zinc#50](https://github.com/AtomiCloud/alcohol.zinc/issues/50)

## [1.22.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.21.0...v1.22.0) (2026-06-28)


### 🐛 Bug Fixes 🐛

* address review — fail-loud SetIntentId, stronger tests, SkippableFact ([dfc636a](https://github.com/AtomiCloud/alcohol.zinc/commit/dfc636a738f3bc3081cece5ddd7321f553539259))
* persist Airwallex intent id between create and confirm ([76175f7](https://github.com/AtomiCloud/alcohol.zinc/commit/76175f71970e5625b1e2d23a003d68041f1a29f6))


### 🧪 Tests 🧪

* add full round-trip integration test for intent-id reuse ([b7c1723](https://github.com/AtomiCloud/alcohol.zinc/commit/b7c1723287966a06e451a5b1f9d801ed33ded1fe))

## [1.21.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.20.0...v1.21.0) (2026-06-23)


### ✨ Features ✨

* habit penalty processing with charity accrual ([bd864f4](https://github.com/AtomiCloud/alcohol.zinc/commit/bd864f45d77ec676935e5c6dac13df1fa7f01a79))
* key charity balance by (charity, currency) for multi-currency ([d722797](https://github.com/AtomiCloud/alcohol.zinc/commit/d7227971ba7fca0c1d89fe0a4c501b0937178719))


### 🐛 Bug Fixes 🐛

* lock penalty + balance rows for race-safe charity accrual ([0e87663](https://github.com/AtomiCloud/alcohol.zinc/commit/0e87663af4dfda9a5aedb4d3e547a9f7560a42ee))
* make Airwallex confirm request_id deterministic for idempotency ([e0afc13](https://github.com/AtomiCloud/alcohol.zinc/commit/e0afc133882c94fee9f9a5f969365dc9ee6b5fb0))
* make MarkCharged idempotent to prevent double charity credit ([110d184](https://github.com/AtomiCloud/alcohol.zinc/commit/110d184a1bca45693e33cce8576db3cf23f0196d))
* regenerate penalty migration + address review feedback ([c084854](https://github.com/AtomiCloud/alcohol.zinc/commit/c084854eeff890fe9b3f95d0c7480ee1c549ce04)), closes [#48](https://github.com/AtomiCloud/alcohol.zinc/issues/48)
* send merchant-initiated fields on Airwallex confirm ([9445158](https://github.com/AtomiCloud/alcohol.zinc/commit/9445158758a4b8375c670c131f31cf4893de5fdd))


### 🧪 Tests 🧪

* default penalty tests to USD + cover same-currency accrual ([48c9b53](https://github.com/AtomiCloud/alcohol.zinc/commit/48c9b5370310aa07e73477aafa63645170d03910))
* strengthen penalty concurrency tests (barrier + fuzz) ([adf56cb](https://github.com/AtomiCloud/alcohol.zinc/commit/adf56cb4205ef51b36d35e2d101311ff2335a89d))

## [1.20.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.19.0...v1.20.0) (2026-06-21)


### ✨ Features ✨

* revoke Airwallex consent on account deletion (best-effort) ([dcbf9a5](https://github.com/AtomiCloud/alcohol.zinc/commit/dcbf9a5acfb8ab34922d216cf0f02dec52ebd9a4))
* self-service account deletion (DELETE /User/Me) ([eb80938](https://github.com/AtomiCloud/alcohol.zinc/commit/eb8093802944c627e8302f38cf5e690c248b140e))


### 🐛 Bug Fixes 🐛

* guarantee best-effort Airwallex revoke + report skipped int tests ([8e35c16](https://github.com/AtomiCloud/alcohol.zinc/commit/8e35c16ddf3926cf5d1267f57ddc81a8084d2808)), closes [#49](https://github.com/AtomiCloud/alcohol.zinc/issues/49)
* run deletion provider-cleanup outside the DB transaction ([9d2d8b2](https://github.com/AtomiCloud/alcohol.zinc/commit/9d2d8b20d9e647d4d6897be6f7c3f67623e1526e))


### 🧪 Tests 🧪

* in-app E2E for Airwallex consent revoke on deletion ([29ab6db](https://github.com/AtomiCloud/alcohol.zinc/commit/29ab6db6b2eef2c3f25d3a24e5e5b06c69d736ac))
* real-DB integration test for account-deletion purge ([a3cdbe0](https://github.com/AtomiCloud/alcohol.zinc/commit/a3cdbe018a2fac9e69abd974cf166f1b18cc276f))

## [1.19.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.18.0...v1.19.0) (2025-10-19)


### ✨ Features ✨

* skip properly exposed ([ab295bb](https://github.com/AtomiCloud/alcohol.zinc/commit/ab295bb3ee229bfe44a30d8a7415ba770ee2e02c))

## [1.18.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.17.1...v1.18.0) (2025-10-19)

## [1.17.1](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.17.0...v1.17.1) (2025-10-12)


### 🐛 Bug Fixes 🐛

* fix payment url ([672957e](https://github.com/AtomiCloud/alcohol.zinc/commit/672957ec2a6e870523c6f08067efb9b668caacfd))

## [1.17.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.16.1...v1.17.0) (2025-10-12)


### ✨ Features ✨

* add log, compress migration ([fe932e5](https://github.com/AtomiCloud/alcohol.zinc/commit/fe932e5624019e615795d7ecda8ff1a25d80a4d2))
* overview endpoint add totalskip and useddkip ([0b2a617](https://github.com/AtomiCloud/alcohol.zinc/commit/0b2a6178ff1a9f9348c6269a206d30dd794d38a9))
* skip, freeze, vacation and monthly ([32f8d04](https://github.com/AtomiCloud/alcohol.zinc/commit/32f8d0405e305bae0965b119d1581c27ceafa07e))


### 🐛 Bug Fixes 🐛

* incorrect restore ([66ed02c](https://github.com/AtomiCloud/alcohol.zinc/commit/66ed02c40eb1abd58512b2c6ede9c4adb2fe3cee))

## [1.16.1](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.16.0...v1.16.1) (2025-10-12)


### 🐛 Bug Fixes 🐛

* set paymentconsent to false ([ec0745d](https://github.com/AtomiCloud/alcohol.zinc/commit/ec0745d70104356707f9484b4ca1484d0ee6e6dd))

## [1.16.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.15.4...v1.16.0) (2025-10-10)


### ✨ Features ✨

* baseline for streak protection mechanism ([c6560f6](https://github.com/AtomiCloud/alcohol.zinc/commit/c6560f6546c4f5b0ad367e349b5e98b329f0c78e))
* delete user endpoint ([258f3cb](https://github.com/AtomiCloud/alcohol.zinc/commit/258f3cb5944b1b46c5b17c925cb0048211ef5261))
* protection basics ([18fd0c0](https://github.com/AtomiCloud/alcohol.zinc/commit/18fd0c075459bf03582e5731ac623f73c75e1207))


### 🐛 Bug Fixes 🐛

* basic seeding ([994d518](https://github.com/AtomiCloud/alcohol.zinc/commit/994d5188710361415f1c6f69830cda9f4a722af5))

## [1.15.4](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.15.3...v1.15.4) (2025-10-07)


### 🐛 Bug Fixes 🐛

* incorrect uri format ([a9f35e6](https://github.com/AtomiCloud/alcohol.zinc/commit/a9f35e615d277985ac3b68f658fae5fb0c4e686f))

## [1.15.3](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.15.2...v1.15.3) (2025-10-07)


### 🐛 Bug Fixes 🐛

* incorrect management client api for logto ([b967376](https://github.com/AtomiCloud/alcohol.zinc/commit/b967376c9460ef3e0cacfdffdbe15e6564f2785b))

## [1.15.2](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.15.1...v1.15.2) (2025-10-07)


### 🐛 Bug Fixes 🐛

* incorrect issuer and domain ([d742cec](https://github.com/AtomiCloud/alcohol.zinc/commit/d742cec3a6c021e0c9431a9e4b3f8f5a544592f3))

## [1.15.1](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.15.0...v1.15.1) (2025-10-07)


### 🐛 Bug Fixes 🐛

* incorrect issuer and domain ([5349000](https://github.com/AtomiCloud/alcohol.zinc/commit/53490006620c8752cc66f5e6377d10befe8d4183))

## [1.15.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.14.0...v1.15.0) (2025-10-07)


### ✨ Features ✨

* debt amount ([9135c09](https://github.com/AtomiCloud/alcohol.zinc/commit/9135c09907808887e4852b649e0f1162d6c486ea))

## [1.14.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.13.0...v1.14.0) (2025-10-07)


### ✨ Features ✨

* check userid for payment endpoint ([d708315](https://github.com/AtomiCloud/alcohol.zinc/commit/d708315d623f88f9e0cb872ceb0304bea639347c))
* disable payment consent ([085fde3](https://github.com/AtomiCloud/alcohol.zinc/commit/085fde341e9e03a524815830d32b91c17c67b86e))


### 🐛 Bug Fixes 🐛

* resolve conflict ([c959015](https://github.com/AtomiCloud/alcohol.zinc/commit/c95901557a21f84ea351d49cd17b05487d7bc1a7))

## [1.13.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.12.1...v1.13.0) (2025-10-06)


### ✨ Features ✨

* better configuration controller ([3f94dd5](https://github.com/AtomiCloud/alcohol.zinc/commit/3f94dd5b3e5d3f2f4ed568c766319fd8759335fc))
* habit with more streaks ([59470e4](https://github.com/AtomiCloud/alcohol.zinc/commit/59470e4d8961dc2c6808fd6553aaccae6dd8f3f1))
* initial working habits overview ([5fe947d](https://github.com/AtomiCloud/alcohol.zinc/commit/5fe947d9c7e94ffb243993c33f89f1ccf299a6d1))


### 🐛 Bug Fixes 🐛

* error and unique ([b6a3746](https://github.com/AtomiCloud/alcohol.zinc/commit/b6a3746fb90e0deb82b3bf44f2c397481af0c02c))
* incorrect service method used ([b4543a7](https://github.com/AtomiCloud/alcohol.zinc/commit/b4543a74cd400c4f5394a8861c541a9b569896ef))
* PR comments ([e01fb0d](https://github.com/AtomiCloud/alcohol.zinc/commit/e01fb0d9d1024bdc7cde696c071fdfb7df9f0a98))

## [1.12.1](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.12.0...v1.12.1) (2025-10-06)


### 🐛 Bug Fixes 🐛

* PR problems ([74cf8ed](https://github.com/AtomiCloud/alcohol.zinc/commit/74cf8ede8140bda96b40a7e2f732423645daa86c))

## [1.12.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.11.0...v1.12.0) (2025-10-06)


### ✨ Features ✨

* charity API and sync ([5dbc1a8](https://github.com/AtomiCloud/alcohol.zinc/commit/5dbc1a8e3f3c09fbceb19cdba38991ed64d1d35f))
* merge from main ([4cb8e2e](https://github.com/AtomiCloud/alcohol.zinc/commit/4cb8e2e72ec0b7d9ec579bb50eda5bb0bdc1e7c1))


### 🐛 Bug Fixes 🐛

* PR problems ([4e310e7](https://github.com/AtomiCloud/alcohol.zinc/commit/4e310e7e725719bb51b17d31afd4ff199d0ce9d7))

## [1.11.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.10.0...v1.11.0) (2025-10-05)


### ✨ Features ✨

* add hasPaymentConsent to jwt claim ([8f4cc5f](https://github.com/AtomiCloud/alcohol.zinc/commit/8f4cc5ffcb03f342192efd9f4c341fd7cb3e34b8))

## [1.10.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.9.0...v1.10.0) (2025-10-05)


### ✨ Features ✨

* add local cache and redis cache for token ([464794f](https://github.com/AtomiCloud/alcohol.zinc/commit/464794f1b0e7a3de1f7bb9861cb22b2150b7d19c))
* payment consent ([107e68b](https://github.com/AtomiCloud/alcohol.zinc/commit/107e68bfe5f4acef04c4ea38ab8709d18dd13035))
* payment init ([068bd52](https://github.com/AtomiCloud/alcohol.zinc/commit/068bd529b27c452c3c4369f86f7a2ae0c978a004))

## [1.9.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.8.0...v1.9.0) (2025-10-05)


### ✨ Features ✨

* allow configuration setup to be synced to auth engine ([bf2f8b8](https://github.com/AtomiCloud/alcohol.zinc/commit/bf2f8b8eb421f42e26181f8ace7caebbcc386996))

## [1.8.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.7.0...v1.8.0) (2025-10-04)


### ✨ Features ✨

* add habit-level timezone configuration ([803cd40](https://github.com/AtomiCloud/alcohol.zinc/commit/803cd409d256b4e7c623e93716ebe57250855d72))
* Failed execution should be based off habit id, not user id ([fb7e132](https://github.com/AtomiCloud/alcohol.zinc/commit/fb7e13246990cea5fa95c91951112989c72283ed))


### 🐛 Bug Fixes 🐛

* dow validator ([a447d82](https://github.com/AtomiCloud/alcohol.zinc/commit/a447d82858947d6c375171ce3b24f838661f582f))
* fix pr comment ([2d86ef3](https://github.com/AtomiCloud/alcohol.zinc/commit/2d86ef3ee2835b44e5259afb91c8412ee6c1e782))
* fix pr comments ([25a09c4](https://github.com/AtomiCloud/alcohol.zinc/commit/25a09c4eb0aa65aeceb7c693d7040987222f5d45))
* remove debug logging ([ff9891f](https://github.com/AtomiCloud/alcohol.zinc/commit/ff9891f2eb6c20457776298d5093b40739ba4211))
* resolve conflict ([b1e255f](https://github.com/AtomiCloud/alcohol.zinc/commit/b1e255ff4ef6a0350073d8b85e366ec55146ffbe))

## [1.7.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.6.0...v1.7.0) (2025-10-04)

## [1.6.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.5.0...v1.6.0) (2025-10-04)


### ✨ Features ✨

* update config to use new logto ([1b734f5](https://github.com/AtomiCloud/alcohol.zinc/commit/1b734f5da7816eeaf413f494c778db1885fd54ae))

## [1.5.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.4.1...v1.5.0) (2025-09-20)


### ✨ Features ✨

* implement habit tracking system with domain model ([b167715](https://github.com/AtomiCloud/alcohol.zinc/commit/b16771551fc365b814a86207e4b304168deca1bc))

## [1.4.1](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.4.0...v1.4.1) (2025-09-20)


### 🐛 Bug Fixes 🐛

* incorrect scope ([1f1199a](https://github.com/AtomiCloud/alcohol.zinc/commit/1f1199a9af40575b406558598da5a29bc9dc5a18))

## [1.4.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.3.0...v1.4.0) (2025-09-20)


### ✨ Features ✨

* enable optionally instally secrets ([3b40c12](https://github.com/AtomiCloud/alcohol.zinc/commit/3b40c1289e0c752d0f7e0518fcfb0ce284b903a7))


### 🐛 Bug Fixes 🐛

* allow installation of CRD ([6ca5f05](https://github.com/AtomiCloud/alcohol.zinc/commit/6ca5f05bc309ee9a0d9a7956344326d9e7bb9006))

## [1.3.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.2.0...v1.3.0) (2025-09-16)


### ✨ Features ✨

* allow claims to be updated when user is created ([4493fae](https://github.com/AtomiCloud/alcohol.zinc/commit/4493fae1ae18854ac3188b6c92e7289e809f0cb8))


### 🐛 Bug Fixes 🐛

* allow some skew ([bf2594b](https://github.com/AtomiCloud/alcohol.zinc/commit/bf2594b07811fa603375686e383b4ee0d4a7a666))
* PR comments ([e28e641](https://github.com/AtomiCloud/alcohol.zinc/commit/e28e641dad9b3ea14baf5ac3b792fce3198309c3))

## [1.2.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.1.0...v1.2.0) (2025-08-30)


### ✨ Features ✨

* remove cache shells ([be0e2ad](https://github.com/AtomiCloud/alcohol.zinc/commit/be0e2ad3d74a81e92900868ae5624f4c52763307))

## [1.1.0](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.0.5...v1.1.0) (2025-08-30)


### ✨ Features ✨

* initial email engine ([bcc7e1b](https://github.com/AtomiCloud/alcohol.zinc/commit/bcc7e1bbbfbcc9520e9b29c8ec2a344343ce5cca))


### 🐛 Bug Fixes 🐛

* embed incorrect folder ([662b323](https://github.com/AtomiCloud/alcohol.zinc/commit/662b3239f8ef23aaa90c24c51aed3644960c861f))
* review comments ([f69b0d4](https://github.com/AtomiCloud/alcohol.zinc/commit/f69b0d411c7d6aab66adb2d4f4be5aae6f952ece))
* review comments ([cacebfc](https://github.com/AtomiCloud/alcohol.zinc/commit/cacebfce10bd7a7cd49649659d6d8cbe08be6bcd))

## [1.0.5](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.0.4...v1.0.5) (2025-08-03)


### 🐛 Bug Fixes 🐛

* upgrade ESO to v1 ([d00ccfd](https://github.com/AtomiCloud/alcohol.zinc/commit/d00ccfd748eb7d664bb977c539b29338c76fe23e))
* upgrade ESO to v1 ([c0a2dc8](https://github.com/AtomiCloud/alcohol.zinc/commit/c0a2dc8ba824184b20019d10b600216181374969))

## [1.0.4](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.0.3...v1.0.4) (2025-06-25)


### 🐛 Bug Fixes 🐛

* allow us to cache nix shells ([2ff12f9](https://github.com/AtomiCloud/alcohol.zinc/commit/2ff12f9d7e7fc08974090de064228b25ee2d574f))

## [1.0.3](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.0.2...v1.0.3) (2025-06-25)


### 🐛 Bug Fixes 🐛

* incorrect stream configuraiton ([41b5887](https://github.com/AtomiCloud/alcohol.zinc/commit/41b58876a702d99e0da9c46ec1dfb8b959900a2a))

## [1.0.2](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.0.1...v1.0.2) (2025-06-24)


### 🐛 Bug Fixes 🐛

* incorrect chart publishing ([897ffc7](https://github.com/AtomiCloud/alcohol.zinc/commit/897ffc7364a9e28d7fc2be5410bef72c0b6caf5e))

## [1.0.1](https://github.com/AtomiCloud/alcohol.zinc/compare/v1.0.0...v1.0.1) (2025-06-21)


### 🐛 Bug Fixes 🐛

* **ci:** sg shouldn't skip ci ([aa01267](https://github.com/AtomiCloud/alcohol.zinc/commit/aa012673d7309f9e8111870f73ef61471c0379ef))

## 1.0.0 (2025-06-21)


### ✨ Features ✨

* initial commit ([d5afafa](https://github.com/AtomiCloud/alcohol.zinc/commit/d5afafad4bcc2c0d29caeb28facc8f19f1893781))


### 🐛 Bug Fixes 🐛

* action use floating pin ([384c65c](https://github.com/AtomiCloud/alcohol.zinc/commit/384c65c9e2b2bdd1f4867e7fc949b38bd9873f30))
* missing prettier config checked into repo ([43943cf](https://github.com/AtomiCloud/alcohol.zinc/commit/43943cf5b093ef8160dd875b65a2eaf3432d6205))
* move publishing to github CI ([779a973](https://github.com/AtomiCloud/alcohol.zinc/commit/779a973c4722b0432ce03beb872478dbd8054d58))
* semantic-release incorrect sub-module versioning pinning ([81cb016](https://github.com/AtomiCloud/alcohol.zinc/commit/81cb016d2b10b9c22427ba56dc67d6c1c37e1771))

# [1.1.0](https://github.com/DotifyBIZ/upkeep/compare/v1.0.0...v1.1.0) (2026-09-18)


### Bug Fixes

* five defects found by running the app ([1ea26cb](https://github.com/DotifyBIZ/upkeep/commit/1ea26cb62bb7e391fd087299ce344635ba598f65))


### Features

* **cleanup:** let people add their own cleanup rules ([e346732](https://github.com/DotifyBIZ/upkeep/commit/e34673211b08b95b2b34ce53fedc60ed201427ec))
* **home:** turn Home into a real dashboard ([935a3b3](https://github.com/DotifyBIZ/upkeep/commit/935a3b3eb0ce9b4f157e78118b4e09333e668083))
* **settings:** show the diagnostic log in the app ([b0a43ff](https://github.com/DotifyBIZ/upkeep/commit/b0a43ff9ce93f5f640b0a45e4fa13acc0260debd))
* **shell:** add a Ctrl+K command palette ([ae16c1f](https://github.com/DotifyBIZ/upkeep/commit/ae16c1f33c7e2c0b41ea85ccfdaaf7f6ceae1c81))
* **shell:** introduce Upkeep on first launch ([3c11be3](https://github.com/DotifyBIZ/upkeep/commit/3c11be35a33e05085884daf6b5295a54a3ff3187))
* **shell:** say when a cleanup finished off-page ([2d4c5f8](https://github.com/DotifyBIZ/upkeep/commit/2d4c5f833bc953e2463a425ce4be293629bf5313))

# 1.0.0 (2026-09-14)


### Bug Fixes

* **app:** declare the hex brush converter on the pages that use it ([bfc3bcf](https://github.com/DotifyBIZ/upkeep/commit/bfc3bcfb6ab113d6151f68e1ace972b18831d43e))
* **drivers:** close the session when leaving the page ([abe8405](https://github.com/DotifyBIZ/upkeep/commit/abe8405168af92f5f8df59e64bfcbb815f3ff7a6))
* **files:** stop claiming a scan found nothing while its results are on screen ([3610461](https://github.com/DotifyBIZ/upkeep/commit/36104615cd74782c08bf2701396f09ef23114cab))
* **release:** write the checksum sidecar without a byte order mark ([38132f1](https://github.com/DotifyBIZ/upkeep/commit/38132f1e3f5541cb259a5189a65fec027f105801))
* **sessions:** back up registry keys with their values, not just their names ([ef4e427](https://github.com/DotifyBIZ/upkeep/commit/ef4e427259dfa0b3511dd92a3a640c326b6ab8ee))
* **sessions:** mark performance and service changes complete so they revert ([fd1732f](https://github.com/DotifyBIZ/upkeep/commit/fd1732f96e8c4a67cd04bb0d17101affc851ed3f))
* **startup:** show the reason when the page fails to load ([6c632d2](https://github.com/DotifyBIZ/upkeep/commit/6c632d259f0c36d7adfb133703a17bf9f14c4d4a))
* **ui:** give the services table the column headers its sibling has ([a740413](https://github.com/DotifyBIZ/upkeep/commit/a740413bc2628a0cccb574f04766299fd47fa514))
* **ui:** line up the file tables, and keep the helper alive when work fails ([78a9488](https://github.com/DotifyBIZ/upkeep/commit/78a94881d3e36b03fccf1ca1b9d8922d76e3af9c))
* **ui:** line up the startup and services tables with their headers ([3e48e7c](https://github.com/DotifyBIZ/upkeep/commit/3e48e7c582ff3f280410f3c0c6e19c7b76f53652))
* **updates:** stop a failed schedule change from half-applying ([bad82f1](https://github.com/DotifyBIZ/upkeep/commit/bad82f111d804217c5e7f31b27688d79da3d145e))


### Features

* **apps:** add the uninstaller page ([0b3d952](https://github.com/DotifyBIZ/upkeep/commit/0b3d95268089373809f3f39a9ec215662a93c63a))
* **apps:** uninstall apps and clear what they leave behind ([2407f83](https://github.com/DotifyBIZ/upkeep/commit/2407f83c821047635903127434ef8d26035662af))
* **cleanup:** add the cleanup vertical end to end ([403bcb8](https://github.com/DotifyBIZ/upkeep/commit/403bcb845bad8950d7c2802ce2515edeb08cbaf4))
* **drivers:** find driver updates and manage the Windows Update schedule ([f44ed51](https://github.com/DotifyBIZ/upkeep/commit/f44ed511bf044e3ff81ad405aad8f4a2b280e78b))
* **files:** find duplicates, large files and where the space went ([b23d6dd](https://github.com/DotifyBIZ/upkeep/commit/b23d6dd31410cd21225ba482ba9f4bf855e59abb))
* **history:** list every session and put one back ([9375edf](https://github.com/DotifyBIZ/upkeep/commit/9375edf5330127c6b7f0d084e35cb1da8d26d6f9))
* **performance:** read and apply the visual-effects settings ([8edf0d5](https://github.com/DotifyBIZ/upkeep/commit/8edf0d5ada9f82c28fe7907d379019cebb1cb1f3))
* scaffold Upkeep with cleanup engine, elevation helper and safety model ([1c0ce2d](https://github.com/DotifyBIZ/upkeep/commit/1c0ce2d13d778e63281b634e657ec120ca979838))
* **services:** let the elevated helper change a service start type ([bcb65da](https://github.com/DotifyBIZ/upkeep/commit/bcb65daf76d3982b14fca52524978613eda1acea))
* **sessions:** put a whole session back in one step ([172c1e0](https://github.com/DotifyBIZ/upkeep/commit/172c1e0dd23fdc6be80605f59617b3b7e788fcb4))
* **settings:** add the settings page ([b1e8773](https://github.com/DotifyBIZ/upkeep/commit/b1e87732e0659f1f77a09543c732eb2f0f4cb2bd))
* **startup:** add the services and performance tabs ([7168e1b](https://github.com/DotifyBIZ/upkeep/commit/7168e1b1a27914f438ab67abb6208f953f17c2df))
* **startup:** list and toggle what starts with Windows ([ef2a052](https://github.com/DotifyBIZ/upkeep/commit/ef2a0529b3bcea3ed2500b9b6e49f9747b8a7874))


### Performance Improvements

* **files:** cap what the page shows and give the lists a real viewport ([5719062](https://github.com/DotifyBIZ/upkeep/commit/5719062fef2fd6b6550629eaf6b64871382323e6))

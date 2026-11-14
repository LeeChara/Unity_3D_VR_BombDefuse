# 효과음 폴더

파일 이름을 **이벤트 이름**으로 해서 이 폴더(하위 폴더 가능)에 넣고,
Unity 메뉴 **BombDefuse > Sound Hookup** 창의 "효과음" 탭에서 **폴더에서 자동 연결**을 누른다.

- 한 이벤트에 소리가 여러 개면 `_1`, `_2`를 붙인다: `player_seated_1.wav`, `player_seated_2.wav` (매번 무작위)
- 총 소리 3개는 이 이름으로 넣고 "총 소리" 탭에서 연결한다: `gun_hammer` (공이치기), `gun_blank` (딸깍), `gun_fire` (발사)
- 이벤트 이름과 무엇이 필요한지는 `docs/08_asset_list.md` 1절, 언제 나는지는 `docs/07_event_list.md`
- 이름이 맞지 않는 파일은 창 아래에 목록으로 나온다

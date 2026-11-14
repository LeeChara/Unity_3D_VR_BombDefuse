# 진행자 목소리 폴더

파일 이름을 **대사 id**(`Data/Dialogue/host_lines.csv`의 id 칸)로 해서 이 폴더에 넣고,
Unity 메뉴 **BombDefuse > Sound Hookup** 창의 "진행자 목소리" 탭에서 **폴더에서 자동 연결**을 누른다.

- 예: `sp01.wav`
- `{bullets}`가 들어간 대사는 숫자마다 따로 녹음할 수 있다: `gh01_1.wav` ~ `gh01_6.wav`. 숫자별 녹음이 없으면 `gh01.wav`를 쓴다
- 같은 창의 **녹음 목록 내보내기**를 누르면 녹음할 대사와 파일 이름이 `recording_list.csv`로 나온다 (엑셀로 열기)
- 녹음이 자막보다 길면 진행자가 녹음이 끝날 때까지 기다린다

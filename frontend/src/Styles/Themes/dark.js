// Bookshelf dark theme: deep ink surfaces with a single indigo accent.
//
// The accent is exported as `readarrRed` because dozens of stylesheets still read var(--readarrRed). Keeping that name
// rebrands all of them at once; rename it when those stylesheets are touched.
const brand = '#6366f1';
const brandLight = '#a5b4fc';
const darkGray = '#888';
const gray = '#adadad';
const black = '#000';
const white = '#fff';
const offWhite = '#f5f7fa';
const purple = '#7a43b6';
const pink = '#ff69b4';
const lightGray = '#ddd';

// Surfaces, darkest to lightest
const surface0 = '#0e1014';
const surface1 = '#14161c';
const surface2 = '#1a1d25';
const surface3 = '#1f232d';
const surface4 = '#262b36';
const line = '#2a2f3b';

module.exports = {
  textColor: '#d7dbe4',
  defaultColor: '#d7dbe4',
  disabledColor: '#8b93a3',
  dimColor: '#6b7280',
  black,
  white,
  offWhite,
  blue: '#06f',
  primaryColor: '#7c83ff',
  selectedColor: '#f9be03',
  successColor: '#34c759',
  dangerColor: '#f05252',
  warningColor: '#f5a524',
  infoColor: brand,
  purple,
  pink,
  readarrRed: brand,
  helpTextColor: '#8b93a3',
  darkGray,
  gray,
  lightGray,

  // Theme Colors

  themeRed: brand,
  themeAlternateRed: '#4f46e5',
  themeDarkRed: '#312e81',
  themeDarkColor: line,
  themeLightColor: '#3a4150',
  pageBackground: surface0,
  pageFooterBackgroud: 'rgba(0, 0, 0, .25)',

  torrentColor: '#00853d',
  usenetColor: '#17b1d9',

  // Labels
  inverseLabelColor: '#ddd',
  inverseLabelTextColor: '#333',
  disabledLabelColor: '#4b5260',
  infoTextColor: white,

  // Links
  defaultLinkHoverColor: '#fff',
  linkColor: '#fff',
  linkHoverColor: brandLight,

  // Header
  pageHeaderBackgroundColor: surface1,

  // Sidebar

  sidebarColor: '#c9cfdb',
  sidebarBackgroundColor: surface1,
  sidebarActiveBackgroundColor: surface3,

  // Toolbar
  toolbarColor: '#c9cfdb',
  toolbarBackgroundColor: '#12151b',
  toolbarMenuItemBackgroundColor: surface3,
  toolbarMenuItemHoverBackgroundColor: surface4,
  toolbarLabelColor: '#c9cfdb',

  // Accents
  borderColor: line,
  inputBorderColor: '#2f3542',
  inputBoxShadowColor: 'rgba(0, 0, 0, 0.075)',
  inputFocusBorderColor: brand,
  inputFocusBoxShadowColor: 'rgba(99, 102, 241, 0.35)',
  inputErrorBorderColor: '#f05252',
  inputErrorBoxShadowColor: 'rgba(240, 82, 82, 0.6)',
  inputWarningBorderColor: '#f5a524',
  inputWarningBoxShadowColor: 'rgba(245, 165, 36, 0.6)',
  colorImpairedGradient: '#707070',
  colorImpairedGradientDark: '#424242',
  colorImpairedDangerGradient: '#d84848',
  colorImpairedWarningGradient: '#e59400',
  colorImpairedPrimaryGradient: '#538cd4',
  colorImpairedGrayGradient: '#9b9b9b',

  //
  // Buttons

  defaultButtonTextColor: '#e6e9ef',
  defaultBackgroundColor: surface3,
  defaultBorderColor: line,
  defaultHoverBackgroundColor: surface4,
  defaultHoverBorderColor: '#3a4150',

  primaryBackgroundColor: brand,
  primaryBorderColor: '#5457e5',
  primaryHoverBackgroundColor: '#575ae6',
  primaryHoverBorderColor: '#4a4dd8',

  successBackgroundColor: '#27c24c',
  successBorderColor: '#26be4a',
  successHoverBackgroundColor: '#24b145',
  successHoverBorderColor: '#1f9c3d',

  warningBackgroundColor: '#ff902b',
  warningBorderColor: '#ff8d26',
  warningHoverBackgroundColor: '#ff8517',
  warningHoverBorderColor: '#fc7800',

  dangerBackgroundColor: '#f05050',
  dangerBorderColor: '#f04b4b',
  dangerHoverBackgroundColor: '#ee3d3d',
  dangerHoverBorderColor: '#ec2626',

  iconButtonDisabledColor: '#7a7a7a',
  iconButtonHoverColor: '#666',
  iconButtonHoverLightColor: '#ccc',

  //
  // Modal

  modalBackdropBackgroundColor: 'rgba(0, 0, 0, 0.65)',
  modalBackgroundColor: surface2,
  modalCloseButtonHoverColor: '#888',

  //
  // Menu
  menuItemColor: '#c9cfdb',
  menuItemHoverColor: brandLight,
  menuItemHoverBackgroundColor: surface4,

  //
  // Toolbar

  toobarButtonHoverColor: brandLight,
  toobarButtonSelectedColor: brandLight,

  //
  // Scroller

  scrollbarBackgroundColor: '#3a4150',
  scrollbarHoverBackgroundColor: '#4a5263',

  //
  // Card

  cardBackgroundColor: surface2,
  cardShadowColor: '#000',
  cardAlternateBackgroundColor: surface2,
  cardCenterBackgroundColor: surface1,

  //
  // Alert

  alertDangerBorderColor: '#a94442',
  alertDangerBackgroundColor: 'rgba(255,0,0,0.1)',
  alertDangerColor: '#ccc',

  alertInfoBorderColor: '#4a4dd8',
  alertInfoBackgroundColor: 'rgba(99,102,241,0.12)',
  alertInfoColor: '#ccc',

  alertSuccessBorderColor: '#3c763d',
  alertSuccessBackgroundColor: 'rgba(0,255,0,0.1)',
  alertSuccessColor: '#ccc',

  alertWarningBorderColor: '#8a6d3b',
  alertWarningBackgroundColor: 'rgba(255,255,0,0.1)',
  alertWarningColor: '#ccc',

  //
  // Slider

  sliderAccentColor: brand,

  //
  // Form

  inputBackgroundColor: surface2,
  inputReadOnlyBackgroundColor: surface1,
  inputHoverBackgroundColor: 'rgba(255, 255, 255, 0.10)',
  inputSelectedBackgroundColor: 'rgba(255, 255, 255, 0.05)',
  advancedFormLabelColor: '#ff902b',
  disabledCheckInputColor: '#ddd',
  disabledInputColor: '#808080',

  //
  // Popover

  popoverTitleBackgroundColor: surface4,
  popoverTitleBorderColor: surface1,
  popoverBodyBackgroundColor: surface2,
  popoverShadowColor: 'rgba(0, 0, 0, 0.2)',
  popoverArrowBorderColor: surface2,

  popoverTitleBackgroundInverseColor: '#595959',
  popoverTitleBorderInverseColor: '#707070',
  popoverShadowInverseColor: 'rgba(0, 0, 0, 0.2)',
  popoverArrowBorderInverseColor: 'rgba(58, 63, 81, 0.75)',

  //
  // Calendar

  calendarTodayBackgroundColor: surface4,
  calendarBackgroundColor: surface1,
  calendarBorderColor: line,
  calendarTextDim: '#eee',
  calendarTextDimAlternate: '#fff',

  //
  // Table

  tableRowHoverBackgroundColor: 'rgba(255, 255, 255, 0.04)',

  //
  // Series

  addSeriesBackgroundColor: surface1,
  seriesBackgroundColor: surface1,
  searchIconContainerBackgroundColor: surface3,
  collapseButtonBackgroundColor: surface1,

  //
  // Season

  seasonBackgroundColor: surface4,
  episodesBackgroundColor: surface1,

  //
  // misc

  progressBarFrontTextColor: white,
  progressBarBackTextColor: white,
  progressBarBackgroundColor: '#2f3542',
  logEventsBackgroundColor: surface1
};

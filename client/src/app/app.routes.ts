import { Routes } from '@angular/router';
import { FileBrowser } from './features/files/file-browser';
import { Search } from './features/search/search';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'files' },
  { path: 'files', component: FileBrowser },
  { path: 'files/:folderId', component: FileBrowser },
  { path: 'search', component: Search },
  { path: '**', redirectTo: 'files' },
];

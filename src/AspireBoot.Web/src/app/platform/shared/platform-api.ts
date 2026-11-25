import { httpResource } from '@angular/common/http';
import { Injectable } from '@angular/core';

/** Mirrors GetAppInfoResponse in AspireBoot.Api/Platform/GetAppInfo. */
export interface AppInfo {
  name: string;
  version: string;
  environment: string;
}

@Injectable({ providedIn: 'root' })
export class PlatformApi {
  /** Public metadata about the running API. */
  appInfo() {
    return httpResource<AppInfo>(() => '/api/platform/info');
  }
}
